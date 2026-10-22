using System.Text.Json;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Persistence;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// <see cref="IDesignNoteStore"/> over the wayfinderDesignNote table, one row per note with the whole
/// note as JSON. <see cref="UpdateAsync"/> is an atomic compare-and-write on <c>Revision</c>, so two
/// replies made at once both land (the service rereads and retries the one that lost).
/// </summary>
public sealed class UmbracoDesignNoteStore(IUmbracoDatabaseFactory databaseFactory) : IDesignNoteStore
{
    public Task<IReadOnlyList<DesignNote>> ListAsync(string blueprintKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        var rows = db.Fetch<DesignNoteSchema>(
            "SELECT * FROM wayfinderDesignNote WHERE BlueprintKey = @0 ORDER BY CreatedUtc, Id", blueprintKey);
        return Task.FromResult<IReadOnlyList<DesignNote>>(rows.Select(Read).ToArray());
    }

    public Task<DesignNote?> GetAsync(string blueprintKey, string noteId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        var row = db.FirstOrDefault<DesignNoteSchema>(
            "SELECT * FROM wayfinderDesignNote WHERE BlueprintKey = @0 AND NoteId = @1", blueprintKey, noteId);
        return Task.FromResult(row is null ? null : Read(row));
    }

    public Task AddAsync(DesignNote note, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        db.Insert(new DesignNoteSchema
        {
            NoteId = note.Id,
            BlueprintKey = note.BlueprintKey,
            Json = Write(note with { Revision = 1 }),
            Revision = 1,
            CreatedUtc = note.CreatedAt.UtcDateTime,
        });
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(DesignNote note, int expectedRevision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        var rows = db.Execute(
            "UPDATE wayfinderDesignNote SET Json = @0, Revision = @1 WHERE BlueprintKey = @2 AND NoteId = @3 AND Revision = @4",
            Write(note with { Revision = expectedRevision + 1 }),
            expectedRevision + 1,
            note.BlueprintKey,
            note.Id,
            expectedRevision);
        return Task.FromResult(rows == 1);
    }

    public Task<bool> DeleteAsync(string blueprintKey, string noteId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        var rows = db.Execute("DELETE FROM wayfinderDesignNote WHERE BlueprintKey = @0 AND NoteId = @1", blueprintKey, noteId);
        return Task.FromResult(rows > 0);
    }

    public Task DeleteAllAsync(string blueprintKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = databaseFactory.CreateDatabase();
        db.Execute("DELETE FROM wayfinderDesignNote WHERE BlueprintKey = @0", blueprintKey);
        return Task.CompletedTask;
    }

    private static string Write(DesignNote note) => JsonSerializer.Serialize(note, ServiceBlueprintJson.WriteOptions);

    private static DesignNote Read(DesignNoteSchema row) =>
        (JsonSerializer.Deserialize<DesignNote>(row.Json, ServiceBlueprintJson.ReadOptions)
            ?? throw new JsonException($"Design note '{row.NoteId}' could not be read.")) with { Revision = row.Revision };
}
