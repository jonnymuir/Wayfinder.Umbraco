using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// SECURITY REGRESSION: <see cref="StageNonceService"/> used to cache only the field list under
/// the nonce's own GUID, with no binding to which instance/user it was minted for and no
/// eviction — despite its own doc comment claiming it "binds form submissions". A nonce is now
/// bound to <c>instanceId</c>/<c>userId</c> at creation and checked on every resolve, and
/// <see cref="ServiceRequestStageService"/>'s whole-page advance path evicts it once consumed.
/// </summary>
public sealed class StageNonceServiceTests
{
    private static readonly IReadOnlyList<FieldRenderPayload> Fields =
    [
        new FieldRenderPayload { FieldKey = "name", Label = "Name", FieldType = "text", Required = true },
    ];

    private static StageNonceService BuildService() => new(
        new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
        Options.Create(new WayfinderServiceDesignOptions()));

    [Fact]
    public async Task ResolveAsync_ReturnsTheFields_ForTheInstanceAndUserItWasCreatedFor()
    {
        var service = BuildService();
        var nonce = await service.CreateAsync("instance-a", "user-a", Fields);

        var resolved = await service.ResolveAsync(nonce, "instance-a", "user-a");

        resolved.Should().BeEquivalentTo(Fields);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_ForADifferentInstance()
    {
        var service = BuildService();
        var nonce = await service.CreateAsync("instance-a", "user-a", Fields);

        var resolved = await service.ResolveAsync(nonce, "instance-b", "user-a");

        resolved.Should().BeNull(
            "a nonce minted for one instance must never resolve against a different instanceId claimed by the caller");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_ForADifferentUser()
    {
        var service = BuildService();
        var nonce = await service.CreateAsync("instance-a", "user-a", Fields);

        var resolved = await service.ResolveAsync(nonce, "instance-a", "user-b");

        resolved.Should().BeNull(
            "a nonce minted for one user must never resolve for a different userId claimed by the caller");
    }

    [Fact]
    public async Task ResolveAsync_CanBeCalledMultipleTimes_BeforeInvalidation()
    {
        // Mirrors a real page visit: one or more async file-upload resolves ahead of the
        // eventual whole-page submission all share the same nonce.
        var service = BuildService();
        var nonce = await service.CreateAsync("instance-a", "user-a", Fields);

        var first = await service.ResolveAsync(nonce, "instance-a", "user-a");
        var second = await service.ResolveAsync(nonce, "instance-a", "user-a");

        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidateAsync_MakesTheNonceUnresolvable()
    {
        var service = BuildService();
        var nonce = await service.CreateAsync("instance-a", "user-a", Fields);

        await service.InvalidateAsync(nonce);
        var resolved = await service.ResolveAsync(nonce, "instance-a", "user-a");

        resolved.Should().BeNull("an invalidated nonce must not be replayable against a second submission");
    }
}
