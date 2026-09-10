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
/// Pickup/putback for the <c>wayfinderServiceRequestWorklist</c> Block Grid block — the
/// caseworker/backstage counterpart to <see cref="WayfinderStageSurfaceController"/>. "Review" an
/// item and its own advance posts are handled by <see cref="WayfinderStageSurfaceController"/>
/// directly (the worklist block renders the same stage UI for a picked instance, via
/// <see cref="ServiceRequestStageService"/>) — this controller only ever does pickup/putback.
///
/// Pickup/putback is a caseworker/backstage action with no legitimate anonymous or citizen use,
/// so it carries a bare <see cref="AuthorizeAttribute"/> — the deny-by-default HTTP boundary
/// (any authenticated member of the host's default scheme). <em>Which</em> queues a caseworker
/// may act on is still the engine's call, enforced by <see cref="ServiceRequestWorklistService"/>
/// passing the host-resolved <c>ActorProfile</c> to <c>PickupWorkItem</c>/<c>PutbackWorkItem</c>.
/// A host that wants a tighter gate registers its own policy and applies it here.
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
    public const string PickupRoutePath = "/umbraco/wayfinder-worklist/pickup";
    public const string PutbackRoutePath = "/umbraco/wayfinder-worklist/putback";

    // Both forms emit @Html.AntiForgeryToken() (wayfinderServiceRequestWorklist.cshtml), so the
    // framework's [ValidateAntiForgeryToken] validates the same __RequestVerificationToken this
    // controller used to check by hand — now in a form the analyzer recognises (CodeQL
    // cs/web/missing-token-validation).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route(PickupRoutePath)]
    public IActionResult Pickup(string instanceId, string cursorId, string returnUrl)
    {
        worklistService.Pickup(HttpContext, instanceId, cursorId);
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route(PutbackRoutePath)]
    public IActionResult Putback(string instanceId, string cursorId, string returnUrl)
    {
        worklistService.Putback(HttpContext, instanceId, cursorId);
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
