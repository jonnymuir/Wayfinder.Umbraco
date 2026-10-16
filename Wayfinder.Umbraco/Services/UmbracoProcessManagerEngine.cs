using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Services.Sanitization;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Services;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// This package's own in-Umbraco, in-process <see cref="IProcessManager"/> — the sole,
/// authoritative engine for every Wayfinder.Umbraco-hosted service request; a distinctly named
/// singleton so it's discoverable in DI registration/debugging. Beyond identity and the
/// built-in <c>user</c> service value (<see cref="CurrentUserServiceValue"/>) no override logic
/// lives here. Every extension point the base engine has is taken from DI and passed straight
/// through, so a host customises the engine by registering services, never by constructing it:
/// an <see cref="IServiceInputsResolver"/> for <c>source: "service"</c> fields, its own
/// <c>ISupportSystemClient</c>s (see docs/guides/support-systems.md in the core Wayfinder repo),
/// <c>IRequestConcurrencyPolicy</c>s, and a replacement <c>IAuditLogStore</c> if the
/// database-backed <see cref="UmbracoAuditLogStore"/> doesn't suit it.
/// </summary>
public sealed class UmbracoProcessManagerEngine(
    ILogger<UmbracoProcessManagerEngine> logger,
    IServiceBlueprintStore definitionStore,
    IServiceContentSanitizer sanitizer,
    IServiceRequestStore instanceStore,
    IAuditLogStore auditLogStore,
    IHttpContextAccessor httpContextAccessor,
    IEnumerable<ISupportSystemClient> supportSystemClients,
    IEnumerable<IRequestConcurrencyPolicy> requestConcurrencyPolicies,
    IBulkDatasetStore? bulkDatasetStore = null,
    IServiceInputsResolver? serviceInputsResolver = null)
    : ProcessManagerEngine(
        logger,
        definitionStore,
        sanitizer,
        serviceInputsResolver is null ? null : serviceInputsResolver.Resolve,
        instanceStore,
        supportSystemClients,
        bulkDatasetStore,
        requestConcurrencyPolicies,
        auditLogStore)
{
    /// <summary>
    /// A new instance is authenticated when the request creating it belongs to a signed-in
    /// user — mirrors the same <c>User.Identity.IsAuthenticated</c> check <c>PrismUserContext</c>
    /// makes, but read directly via <see cref="IHttpContextAccessor"/> rather than injecting
    /// the scoped <c>IPrismUserContext</c> into this singleton engine (which would capture a
    /// stale request the first time it resolved).
    /// </summary>
    protected override bool ResolveIsAuthenticated(string tenantId, string userId) =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Adds the built-in <see cref="CurrentUserServiceValue"/> when the blueprint declares a
    /// <c>source: "service"</c> field named <c>user</c>, so a blueprint can default inputs from
    /// the signed-in user declaratively. A host's own <see cref="IServiceInputsResolver"/> that supplies
    /// <c>user</c> itself takes precedence.
    /// </summary>
    protected override IReadOnlyDictionary<string, object?>? ResolveServiceInputs(
        ServiceRequest instance,
        ServiceBlueprint definition,
        StageDefinition stage)
    {
        var inputs = base.ResolveServiceInputs(instance, definition, stage);

        var declaresUser = definition.Calculations?.Fields.TryGetValue(CurrentUserServiceValue.FieldName, out var field) == true
            && string.Equals(field.Source, "service", StringComparison.OrdinalIgnoreCase);
        if (!declaresUser || inputs?.ContainsKey(CurrentUserServiceValue.FieldName) == true)
        {
            return inputs;
        }

        var merged = inputs is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(inputs, StringComparer.Ordinal);
        merged[CurrentUserServiceValue.FieldName] = CurrentUserServiceValue.Build(httpContextAccessor.HttpContext?.User);
        return merged;
    }

    /// <summary>
    /// Resolves a <c>file-upload</c> field's stored reference for a download endpoint — reuses
    /// the exact same ownership check (<see cref="ProcessManagerEngine.TryGetAccessibleInstance"/>)
    /// every other instance access goes through, rather than a separate re-derivation. Returns
    /// <see langword="null"/> for an unknown instance, a requester who doesn't own it, or a
    /// field with no uploaded file — callers should treat all three identically (404), not
    /// distinguish "not found" from "not yours".
    /// </summary>
    public ServiceRequestFileReference? TryGetOwnedFileReference(
        string instanceId,
        string tenantId,
        string userId,
        ActorProfile accessProfile,
        string fieldKey)
    {
        var instance = TryGetAccessibleInstance(instanceId, tenantId, userId, accessProfile);
        if (instance is null)
        {
            return null;
        }

        return instance.FieldValues.TryGetValue(fieldKey, out var raw)
            ? ServiceRequestFileReference.FromFieldValue(raw)
            : null;
    }

    /// <summary>
    /// The same ownership check <see cref="TryGetOwnedFileReference"/> performs, for a caller
    /// that needs to authorize against an instance before any file exists yet — the async
    /// upload endpoint, which must verify the requester owns the instance it's about to write a
    /// new file against.
    /// </summary>
    public bool IsOwnedInstance(
        string instanceId,
        string tenantId,
        string userId,
        ActorProfile accessProfile)
    {
        return TryGetAccessibleInstance(instanceId, tenantId, userId, accessProfile) is not null;
    }
}
