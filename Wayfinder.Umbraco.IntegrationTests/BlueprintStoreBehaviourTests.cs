using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Engine.Abstractions;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// The blueprint store serves reads from a cached snapshot (hosts resolve every caller's queues
/// from it on each request), so a save or delete through the store must show up in the very next
/// read — a stale snapshot would silently hand a caller yesterday's queues.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class BlueprintStoreBehaviourTests(ReferenceAppFactory factory)
{
    [Fact]
    public async Task ASavedBlueprint_IsVisibleToTheNextReadAndGoneOnceDeleted()
    {
        var store = factory.Services.GetRequiredService<IServiceBlueprintSourceStore>();
        var key = $"cache-probe-{Guid.NewGuid():N}";
        var template = (await store.LoadAsync("reference-demo"))!;

        // Prime the snapshot so the save below has to invalidate it.
        (await store.ListAsync()).Should().NotContain(s => s.DefinitionKey == key);

        var saved = await store.SaveAsync(template with { DefinitionKey = key, DisplayName = "Cache probe" }, expectedVersion: 0);
        saved.Saved.Should().BeTrue();
        try
        {
            (await store.ListAsync()).Should().Contain(s => s.DefinitionKey == key && s.DisplayName == "Cache probe");
            (await store.LoadAsync(key))!.Version.Should().Be(1);

            var updated = await store.SaveAsync(
                (await store.LoadAsync(key))! with { DisplayName = "Renamed" }, expectedVersion: 1);
            updated.Saved.Should().BeTrue();
            (await store.LoadAsync(key))!.Version.Should().Be(2, "an update must not be masked by the cached copy");
            (await store.ListAsync()).Should().Contain(s => s.DefinitionKey == key && s.DisplayName == "Renamed");
        }
        finally
        {
            (await store.DeleteAsync(key)).Should().BeTrue();
        }

        (await store.ListAsync()).Should().NotContain(s => s.DefinitionKey == key);
        (await store.LoadAsync(key)).Should().BeNull();
    }
}
