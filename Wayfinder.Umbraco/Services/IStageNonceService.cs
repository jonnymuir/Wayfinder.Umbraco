using Wayfinder.Umbraco.Models;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Generates and validates workflow step nonces used to bind form submissions
/// to their server-authoritative field definitions, preventing field injection
/// and constraint bypass attacks.
/// </summary>
public interface IStageNonceService
{
    /// <summary>
    /// Creates a nonce bound to <paramref name="instanceId"/>/<paramref name="userId"/>, caches
    /// the step's field definitions under it, and returns the nonce string.
    /// </summary>
    Task<string> CreateAsync(
        string instanceId, string userId, IReadOnlyList<FieldRenderPayload> fields, CancellationToken ct = default);

    /// <summary>
    /// Resolves a nonce back to its field definitions — but only when
    /// <paramref name="instanceId"/>/<paramref name="userId"/> match what it was created with
    /// (see <see cref="CreateAsync"/>). Does not evict the entry: a single render's nonce is
    /// legitimately resolved multiple times before the eventual whole-page submission (once per
    /// async <c>file-upload</c> field a visitor fills in ahead of it) — see
    /// <see cref="InvalidateAsync"/> for the caller that actually consumes a submission. Returns
    /// null for an expired/unknown nonce or one presented with a mismatched instance/user —
    /// deliberately indistinguishable from each other.
    /// </summary>
    Task<IReadOnlyList<FieldRenderPayload>?> ResolveAsync(
        string nonce, string instanceId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Evicts a nonce so it can't be resolved again — called once a whole-page submission has
    /// actually used it (<see cref="Controllers.WayfinderStageSurfaceController.Advance"/>), to
    /// bound replay of the same submission to "before the first successful advance" rather than
    /// "until the nonce's own TTL expires". Never called for an async <c>file-upload</c>'s own
    /// resolve — that nonce still has a whole-page submission ahead of it to serve.
    /// </summary>
    Task InvalidateAsync(string nonce, CancellationToken ct = default);
}
