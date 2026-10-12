namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Which queues the saved blueprints declare for a given actor — the lookup a host's
/// <see cref="Configuration.WayfinderServiceDesignOptions.ResolveAccessProfile"/> needs on every
/// citizen/caseworker request. Answers are plain strings, so they can be shared and cached
/// without anyone being able to alter what other requests see.
/// </summary>
public interface IBlueprintQueueDirectory
{
    /// <summary>
    /// Every queue key, across every saved blueprint, whose declared <c>Actor</c> matches
    /// <paramref name="actor"/> (case-insensitive), without duplicates.
    /// </summary>
    Task<IReadOnlyList<string>> QueueKeysForActorAsync(string actor, CancellationToken ct = default);
}
