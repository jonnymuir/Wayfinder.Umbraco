using System.Text.Json;
using System.Text.Json.Serialization;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Umbraco.Persistence;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// <see cref="IServiceBlueprintSourceStore"/> for backoffice-authored CMS Service Blueprint
/// definitions — persists to the wayfinderServiceBlueprint table (uSync-portable) rather than
/// MockBusinessApp's memory-only reference store. A successful save is pushed straight into
/// <paramref name="engine"/> so the live engine reflects it immediately, matching the promise
/// the AI-authoring surface already makes.
/// <para/>
/// Reads are served from an in-memory snapshot of the whole table: hosts resolve a caller's queues
/// from the saved blueprints on every citizen/caseworker request (see
/// <see cref="Configuration.WayfinderServiceDesignOptions.ResolveAccessProfile"/>), which used to
/// cost one query plus one query and JSON parse per blueprint, every time. Saves and deletes
/// through this store drop the snapshot at once; a write that bypasses it (a uSync import, another
/// server in a load-balanced deployment) becomes visible when the snapshot's
/// <see cref="SnapshotLifetime"/> lapses.
/// </summary>
public sealed class UmbracoServiceBlueprintStore(
    IUmbracoDatabaseFactory databaseFactory,
    IProcessManager engine,
    TimeProvider? timeProvider = null) : IServiceBlueprintSourceStore
{
    public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object snapshotGate = new();
    private Snapshot? snapshot;
    // Bumped by every write so a read that started before it can't re-cache the old rows.
    private long generation;

    // Lazy per blueprint: one row that fails to parse still fails only its own LoadAsync.
    private sealed record Snapshot(
        DateTimeOffset LoadedAt,
        IReadOnlyList<ServiceBlueprintSourceSummary> Summaries,
        IReadOnlyDictionary<string, Lazy<ServiceBlueprint?>> Blueprints);

    private Snapshot GetSnapshot()
    {
        long startedAt;
        lock (snapshotGate)
        {
            if (snapshot is { } current && clock.GetUtcNow() - current.LoadedAt < SnapshotLifetime)
            {
                return current;
            }

            startedAt = generation;
        }

        using var db = databaseFactory.CreateDatabase();
        var rows = db.Fetch<ServiceBlueprintSchema>("SELECT * FROM wayfinderServiceBlueprint ORDER BY DefinitionKey");
        var loaded = new Snapshot(
            clock.GetUtcNow(),
            rows.Select(row => new ServiceBlueprintSourceSummary(row.DefinitionKey, row.DisplayName)).ToArray(),
            rows.ToDictionary(
                row => row.DefinitionKey,
                row => new Lazy<ServiceBlueprint?>(() =>
                    JsonSerializer.Deserialize<ServiceBlueprint>(row.Json, ReadOptions) is { } blueprint
                        ? blueprint with { Version = row.Version }
                        : null)));

        lock (snapshotGate)
        {
            if (generation == startedAt)
            {
                snapshot = loaded;
            }
        }

        return loaded;
    }

    private void InvalidateSnapshot()
    {
        lock (snapshotGate)
        {
            generation++;
            snapshot = null;
        }
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Component is a [JsonPolymorphic] type; not every blueprint's components have
        // "type" written first, so this must be relaxed — matches FilesystemServiceBlueprintSourceStore.
        AllowOutOfOrderMetadataProperties = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public Task<IReadOnlyList<ServiceBlueprintSourceSummary>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetSnapshot().Summaries);
    }

    public Task<ServiceBlueprint?> LoadAsync(string definitionKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            GetSnapshot().Blueprints.TryGetValue(definitionKey, out var blueprint) ? blueprint.Value : null);
    }

    public Task<ServiceBlueprintSaveResult> SaveAsync(
        ServiceBlueprint blueprint, int expectedVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();

        var existing = db.FirstOrDefault<ServiceBlueprintSchema>(
            "SELECT * FROM wayfinderServiceBlueprint WHERE DefinitionKey = @0", blueprint.DefinitionKey);

        if (existing is null)
        {
            if (expectedVersion != 0)
            {
                return Task.FromResult(new ServiceBlueprintSaveResult(Saved: false, CurrentVersion: 0, Location: "wayfinderServiceBlueprint"));
            }

            ct.ThrowIfCancellationRequested();
            var newRow = new ServiceBlueprintSchema
            {
                DefinitionKey = blueprint.DefinitionKey,
                DisplayName = blueprint.DisplayName,
                Json = JsonSerializer.Serialize(blueprint with { Version = 1 }, WriteOptions),
                Version = 1,
                UpdatedUtc = DateTime.UtcNow
            };
            db.Insert(newRow);
            InvalidateSnapshot();

            engine.UpdateDefinition(blueprint.DefinitionKey, blueprint with { Version = 1 });
            return Task.FromResult(new ServiceBlueprintSaveResult(Saved: true, CurrentVersion: 1, Location: "wayfinderServiceBlueprint"));
        }

        if (existing.Version != expectedVersion)
        {
            return Task.FromResult(new ServiceBlueprintSaveResult(Saved: false, CurrentVersion: existing.Version, Location: "wayfinderServiceBlueprint"));
        }

        ct.ThrowIfCancellationRequested();
        var newVersion = expectedVersion + 1;
        var toSave = blueprint with { Version = newVersion };

        // Atomic compare-and-swap: only the writer that still sees `expectedVersion` wins the race.
        var rowsAffected = db.Execute(
            "UPDATE wayfinderServiceBlueprint SET DisplayName = @0, Json = @1, Version = @2, UpdatedUtc = @3 " +
            "WHERE DefinitionKey = @4 AND Version = @5",
            blueprint.DisplayName,
            JsonSerializer.Serialize(toSave, WriteOptions),
            newVersion,
            DateTime.UtcNow,
            blueprint.DefinitionKey,
            expectedVersion);

        if (rowsAffected == 0)
        {
            var current = db.FirstOrDefault<ServiceBlueprintSchema>(
                "SELECT * FROM wayfinderServiceBlueprint WHERE DefinitionKey = @0", blueprint.DefinitionKey);
            return Task.FromResult(new ServiceBlueprintSaveResult(
                Saved: false, CurrentVersion: current?.Version ?? existing.Version, Location: "wayfinderServiceBlueprint"));
        }

        InvalidateSnapshot();
        engine.UpdateDefinition(blueprint.DefinitionKey, toSave);
        return Task.FromResult(new ServiceBlueprintSaveResult(Saved: true, CurrentVersion: newVersion, Location: "wayfinderServiceBlueprint"));
    }

    public Task<bool> DeleteAsync(string definitionKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        var rowsAffected = db.Execute(
            "DELETE FROM wayfinderServiceBlueprint WHERE DefinitionKey = @0", definitionKey);

        if (rowsAffected > 0)
        {
            InvalidateSnapshot();
            engine.RemoveDefinition(definitionKey);
        }

        return Task.FromResult(rowsAffected > 0);
    }
}
