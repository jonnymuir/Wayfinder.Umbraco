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
/// The Umbraco-native way for a content-composed page to post a form back — a
/// <see cref="SurfaceController"/> is exactly what Umbraco itself provides for this, unlike the
/// route-hijacking <c>RenderController</c> a fixed-URL page owns outright. Handles the POST leg
/// for the <c>wayfinderServiceRequestStage</c> Block Grid block; the GET/render leg lives in the
/// block's own partial view, both sharing <see cref="ServiceRequestStageService"/> so there's one
/// implementation, not two.
/// </summary>
public class WayfinderStageSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    ServiceRequestStageService stageService)
    : SurfaceController(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    /// <summary>
    /// A fixed, explicit route rather than Umbraco's own dynamic/encrypted surface-form URL
    /// generation (<c>Html.BeginUmbracoForm</c>) — this block's form already carries its own
    /// <c>ReturnUrl</c> hidden field (see <c>StageFormTagHelper</c>) to redirect back to whatever
    /// page it was rendered on, so nothing here needs Umbraco's own page-identity encryption; a
    /// plain, predictable route is simpler and needs no live instance to verify the URL shape.
    /// </summary>
    public const string RoutePath = "/umbraco/wayfinder-stage/advance";

    // wayfinder-stage-form (StageFormTagHelper) emits __RequestVerificationToken, so
    // [ValidateAntiForgeryToken] validates the same token this action used to check by hand —
    // now in a form CodeQL recognises (cs/web/missing-token-validation).
    // [AllowAnonymous] is deliberate: the citizen stage journey supports not-yet-signed-in
    // applicants (GOV.UK anonymous-start pattern — see the reference app's ResolveAccessProfile,
    // which resolves an anonymous caller to a citizen profile). Authorization is enforced
    // downstream by the engine — AdvanceAsync is scoped by the host-resolved AccessProfile and
    // the instanceId, and cross-citizen isolation is covered by the reference app's
    // cross-citizen-isolation Playwright spec. [ValidateAntiForgeryToken] still applies.
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [Route(RoutePath)]
    public async Task<IActionResult> Advance()
    {
        var result = await stageService.AdvanceAsync(HttpContext, Request.Form);

        // PRG: a failed submission's problems and values ride in TempData, tied to their request,
        // for the next render of that request to show. TempData is a controller concern, so it is
        // written here rather than by the service.
        result.Flash?.Keep(TempData);
        if (result.MovedOn)
        {
            StageFlash.KeepMovedOn(TempData, result.InstanceId);
        }

        return Redirect(Url.IsLocalUrl(result.ReturnUrl) ? result.ReturnUrl : "/");
    }
}
