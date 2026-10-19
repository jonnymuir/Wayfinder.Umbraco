using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Opening a blueprint's page picks up the person's own most recent request, whatever state it is in
/// (unsent, waiting, answered, closed), and starts one only when they have none; a "start again" link
/// (the blueprint's <c>allowManualRestart</c>) starts another on purpose. Suits a conversation such as a
/// contact page. Give the blueprint <c>requestPolicy</c> <c>"multiple"</c>, so signing in claims every
/// request a visitor had, not just one, and register this for its key:
/// <c>services.AddSingleton&lt;IRequestConcurrencyPolicy&gt;(new LatestRequestPolicy("contact"));</c>
/// </summary>
public sealed class LatestRequestPolicy(params string[] blueprintKeys) : IRequestConcurrencyPolicy
{
    public IReadOnlyList<string> DefinitionKeys { get; } = blueprintKeys;

    public Task<RequestConcurrencyDecision> EvaluateAsync(
        ServiceBlueprint definition,
        string tenantId,
        string userId,
        ActorProfile accessProfile,
        IReadOnlyList<ServiceRequest> candidateInstances,
        CancellationToken ct = default)
    {
        // The candidates are everyone's requests for this blueprint in the tenant: only the caller's own count.
        var latest = candidateInstances
            .Where(request => string.Equals(request.UserId, userId, StringComparison.Ordinal) && !request.IsAborted)
            .MaxBy(request => request.UpdatedAt);

        return Task.FromResult(latest is null
            ? RequestConcurrencyDecision.AllowNew()
            : RequestConcurrencyDecision.ReuseExisting(latest));
    }
}
