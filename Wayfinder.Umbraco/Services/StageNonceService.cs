using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Models;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Generates and validates workflow step nonces using IDistributedCache.
/// Nonces bind form submissions to server-authoritative field definitions,
/// preventing field injection and constraint bypass attacks.
/// </summary>
public class StageNonceService : IStageNonceService
{
    private readonly IDistributedCache _cache;
    private readonly WayfinderServiceDesignOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public StageNonceService(
        IDistributedCache cache,
        IOptions<WayfinderServiceDesignOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    private sealed record NoncePayload(string InstanceId, string UserId, IReadOnlyList<FieldRenderPayload> Fields);

    /// <summary>
    /// Creates a nonce bound to <paramref name="instanceId"/>/<paramref name="userId"/>, caches
    /// the step's field definitions under it, and returns the nonce string.
    /// </summary>
    public async Task<string> CreateAsync(
        string instanceId, string userId, IReadOnlyList<FieldRenderPayload> fields, CancellationToken ct = default)
    {
        var nonce = Guid.NewGuid().ToString("N");

        var json = JsonSerializer.SerializeToUtf8Bytes(new NoncePayload(instanceId, userId, fields), JsonOptions);

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.NonceExpiry
        };

        await _cache.SetAsync(CacheKey(nonce), json, cacheOptions, ct);

        return nonce;
    }

    /// <summary>
    /// Resolves a nonce back to its field definitions — only when it was created for this exact
    /// <paramref name="instanceId"/>/<paramref name="userId"/>. Returns null for an
    /// expired/unknown nonce or a mismatched instance/user — deliberately indistinguishable from
    /// each other. See <see cref="InvalidateAsync"/> for eviction.
    /// </summary>
    public async Task<IReadOnlyList<FieldRenderPayload>?> ResolveAsync(
        string nonce, string instanceId, string userId, CancellationToken ct = default)
    {
        var json = await _cache.GetAsync(CacheKey(nonce), ct);

        if (json == null)
            return null;

        var payload = JsonSerializer.Deserialize<NoncePayload>(json, JsonOptions);
        if (payload is null
            || !string.Equals(payload.InstanceId, instanceId, StringComparison.Ordinal)
            || !string.Equals(payload.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        return payload.Fields;
    }

    /// <inheritdoc/>
    public Task InvalidateAsync(string nonce, CancellationToken ct = default) =>
        _cache.RemoveAsync(CacheKey(nonce), ct);

    private static string CacheKey(string nonce) => $"wayfinder:workflow:nonce:{nonce}";
}
