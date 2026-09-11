using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// Pickup/putback/review-redirect for the <c>wayfinderServiceRequestWorklist</c> Block Grid
/// block. Actually rendering a picked instance is the Block Grid partial's own job (the same
/// stage UI, via <see cref="ServiceRequestStageService"/>) — a Block Grid component can only
/// render inline on whichever content page hosts it, not at a controller's own URL, so this
/// controller's Review action does nothing but redirect there.
///
/// The route shape (<c>{ItemUrlPrefix}/{{blueprintKey}}/{{instanceId}}[/pickup|/putback]</c>,
/// a <c>returnTo</c> query parameter/hidden field) matches exactly what
/// <see cref="Wayfinder.Engine.Worklist.WorklistRenderer.RenderWorklistBody"/> and
/// <see cref="Wayfinder.Engine.Worklist.WorklistRenderer.RenderPickupPutbackControl"/> render, so
/// the Block Grid partial calls that shared renderer unmodified rather than building its own
/// worklist markup. <c>blueprintKey</c> isn't read by the actions below — everything here only
/// needs <c>instanceId</c> (plus <c>cursorId</c> for pickup/putback) — it's a route segment
/// purely so this host's URLs match the shared renderer's shape.
///
/// Every action here is a caseworker/backstage concern with no legitimate anonymous or citizen
/// use, so the controller carries a bare <see cref="AuthorizeAttribute"/> — the deny-by-default
/// HTTP boundary (any authenticated member of the host's default scheme). <em>Which</em> queues
/// or instances a caseworker may act on is still the engine's call, enforced by
/// <see cref="ServiceRequestWorklistService"/> passing the host-resolved <c>ActorProfile</c> to
/// <c>PickupWorkItem</c>/<c>PutbackWorkItem</c>, and by <c>GetCurrent</c>'s own ownership check
/// for the instance the Review redirect lands on. A host that wants a tighter gate registers its
/// own policy and applies it here.
/// </summary>
[Authorize]
public class WayfinderWorklistSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    ServiceRequestWorklistService worklistService)
    : SurfaceController(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    public const string ItemUrlPrefix = "/umbraco/wayfinder-worklist";

    // The Review/View link RenderWorklistBody renders carries the listing page's own URL as a
    // returnTo query parameter (added specifically for this host — see that method's own
    // remarks) precisely because this GET has no page of its own to render into: it 302s back to
    // wherever the worklist block actually lives, with ?instanceId=/&blueprintKey= appended so
    // that page's own item-detail branch (see wayfinderServiceRequestWorklist.cshtml) picks it
    // up. No returnTo (a caller that bypassed the renderer) falls back to home rather than open-
    // redirecting anywhere off-site.
    [HttpGet]
    [Route(ItemUrlPrefix + "/{blueprintKey}/{instanceId}")]
    public IActionResult Review(string blueprintKey, string instanceId, string? returnTo)
    {
        var target = Url.IsLocalUrl(returnTo) ? returnTo! : "/";
        var separator = target.Contains('?') ? '&' : '?';
        return Redirect($"{target}{separator}instanceId={Uri.EscapeDataString(instanceId)}&blueprintKey={Uri.EscapeDataString(blueprintKey)}");
    }

    // [ValidateAntiForgeryToken] validates the __RequestVerificationToken
    // RenderPickupPutbackControl renders into its own pickup/putback forms.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route(ItemUrlPrefix + "/{blueprintKey}/{instanceId}/pickup")]
    public IActionResult Pickup(string blueprintKey, string instanceId, string cursorId, string returnTo)
    {
        worklistService.Pickup(HttpContext, instanceId, cursorId);
        return Redirect(Url.IsLocalUrl(returnTo) ? returnTo : "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route(ItemUrlPrefix + "/{blueprintKey}/{instanceId}/putback")]
    public IActionResult Putback(string blueprintKey, string instanceId, string cursorId, string returnTo)
    {
        worklistService.Putback(HttpContext, instanceId, cursorId);
        return Redirect(Url.IsLocalUrl(returnTo) ? returnTo : "/");
    }
}
