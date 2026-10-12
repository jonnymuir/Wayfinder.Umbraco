using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// The blueprint store serves reads from a cached snapshot (hosts resolve every caller's queues
/// from it on each request), so a save or delete through the store must show up in the very next
/// read — a stale snapshot would silently hand a caller yesterday's queues. And because that
/// snapshot is shared by every request, what one caller does to a loaded blueprint must never be
/// visible to another. Each test saves its own uniquely-keyed blueprint, so none depends on what
/// the reference host happened to seed.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class BlueprintStoreBehaviourTests(ReferenceAppFactory factory)
{
    private IServiceBlueprintSourceStore Store => factory.Services.GetRequiredService<IServiceBlueprintSourceStore>();

    private static ServiceBlueprint NewBlueprint(string key, string displayName = "Cache probe") => new()
    {
        DefinitionKey = key,
        DisplayName = displayName,
        Queues = [new QueueDefinition { Key = $"{key}-queue", DisplayName = "Probe queue", Actor = "probe-actor" }],
    };

    private async Task<string> SaveNew(string displayName = "Cache probe")
    {
        var key = $"cache-probe-{Guid.NewGuid():N}";
        (await Store.SaveAsync(NewBlueprint(key, displayName), expectedVersion: 0)).Saved.Should().BeTrue();
        return key;
    }

    [Fact]
    public async Task ASavedBlueprint_IsVisibleToTheNextReadAndGoneOnceDeleted()
    {
        var key = $"cache-probe-{Guid.NewGuid():N}";

        // Prime the snapshot so the save below has to invalidate it.
        (await Store.ListAsync()).Should().NotContain(s => s.DefinitionKey == key);

        (await Store.SaveAsync(NewBlueprint(key), expectedVersion: 0)).Saved.Should().BeTrue();
        try
        {
            (await Store.ListAsync()).Should().Contain(s => s.DefinitionKey == key && s.DisplayName == "Cache probe");
            (await Store.LoadAsync(key))!.Version.Should().Be(1);

            var updated = await Store.SaveAsync(
                (await Store.LoadAsync(key))! with { DisplayName = "Renamed" }, expectedVersion: 1);
            updated.Saved.Should().BeTrue();
            (await Store.LoadAsync(key))!.Version.Should().Be(2, "an update must not be masked by the cached copy");
            (await Store.ListAsync()).Should().Contain(s => s.DefinitionKey == key && s.DisplayName == "Renamed");
        }
        finally
        {
            (await Store.DeleteAsync(key)).Should().BeTrue();
        }

        (await Store.ListAsync()).Should().NotContain(s => s.DefinitionKey == key);
        (await Store.LoadAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task EachLoadOfABlueprint_IsItsOwnCopy()
    {
        var key = await SaveNew();
        try
        {
            var first = await Store.LoadAsync(key);
            var second = await Store.LoadAsync(key);

            first.Should().NotBeNull();
            first.Should().NotBeSameAs(second, "a caller altering its copy must not be able to change what the next request sees");
            first.Should().BeEquivalentTo(second);
        }
        finally
        {
            await Store.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task TheQueueDirectory_FollowsASaveAndADeleteImmediately()
    {
        var directory = factory.Services.GetRequiredService<IBlueprintQueueDirectory>();
        (await directory.QueueKeysForActorAsync("probe-actor")).Should().BeEmpty();

        var key = await SaveNew();
        try
        {
            (await directory.QueueKeysForActorAsync("probe-actor")).Should().Equal($"{key}-queue");
            (await directory.QueueKeysForActorAsync("PROBE-ACTOR")).Should().Equal(new[] { $"{key}-queue" }, "actor matching is case-insensitive");
        }
        finally
        {
            await Store.DeleteAsync(key);
        }

        (await directory.QueueKeysForActorAsync("probe-actor")).Should().BeEmpty();
    }
}
