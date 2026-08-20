using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Configuration;

/// <summary>
/// Configuration options for the Wayfinder service design engine.
/// Bind from configuration section "Wayfinder".
/// </summary>
public class WayfinderServiceDesignOptions
{
    /// <summary>
    /// How long a workflow step nonce remains valid in the distributed cache.
    /// Defaults to 2 hours. Increase for slow multi-step workflows.
    /// </summary>
    public TimeSpan NonceExpiry { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Required. How this host resolves the tenant id for the current request — this package
    /// carries no multi-tenancy opinion of its own (see this class's own remarks elsewhere), so
    /// a host (Prism, or any other Umbraco site) must supply this. The engine is authoritative
    /// and in-process (<see cref="Services.UmbracoProcessManagerEngine"/>) — there is no longer
    /// a remote "Business App" to derive tenant identity from a forwarded bearer token instead.
    /// </summary>
    public Func<HttpContext, string>? ResolveTenantId { get; set; }

    /// <summary>
    /// Required. How this host resolves the accessing actor's <see cref="ActorProfile"/> for the
    /// current request — see <see cref="ResolveTenantId"/>'s own remarks.
    /// </summary>
    public Func<HttpContext, ActorProfile>? ResolveAccessProfile { get; set; }

    /// <summary>Defaults to reading <see cref="ClaimTypes.NameIdentifier"/> — override only if
    /// this host resolves the acting user id differently.</summary>
    public Func<HttpContext, string> ResolveUserId { get; set; } = DefaultResolveUserId;

    private static string DefaultResolveUserId(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException("Authenticated request has no NameIdentifier claim.");

    /// <summary>
    /// Base route the built-in <c>file-upload</c>/<c>summary-list</c> partials build their
    /// async-upload and download links against — <c>{base}/upload/{instanceId}/{fieldKey}</c>
    /// and <c>{base}/files/{instanceId}/{fieldKey}</c>. Deliberately NOT hardcoded to a specific
    /// controller here: file upload/download needs an ownership check (does this actor own this
    /// instance?), and Wayfinder.Umbraco carries no access-control opinion of its own (see
    /// SingleQueueStructuralValidator's remarks on the same theme) — a host owns its own
    /// upload/download controllers (mirroring <c>ServiceRequestPageController{T}</c>'s own
    /// generic file-save call in <c>HandlePost</c>, which needs no ownership check because it's
    /// already scoped to the instance the current page render resolved) and only needs to change
    /// this if its controllers aren't mounted at the default. Defaults to "/service-request" —
    /// the convention this package's own reference host (UmbracoPrism.TestSite) uses.
    /// </summary>
    public string FileEndpointBasePath { get; set; } = "/service-request";

    /// <summary>
    /// User group aliases authorized to call <see cref="Controllers.ServiceBlueprintAuthoringController"/>
    /// (enforced by <see cref="WayfinderAdminHandler"/>, policy
    /// <see cref="WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin"/>). Blueprints itself
    /// lives under Umbraco's built-in Settings section, so nav visibility needs no configuration
    /// of its own — any backoffice user with Settings access sees it — but the authoring API
    /// is a separate boundary: without this, an authenticated backoffice user without Settings
    /// access could still reach the API directly. Defaults to just the built-in Administrators
    /// group (<c>Umbraco.Cms.Core.Constants.Security.AdminGroupAlias</c>) — a host adds its own
    /// group aliases here if other roles should also be able to author.
    /// </summary>
    public string[] AdminGroupAliases { get; set; } = [global::Umbraco.Cms.Core.Constants.Security.AdminGroupAlias];
}
