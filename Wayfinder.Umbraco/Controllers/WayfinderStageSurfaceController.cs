using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    ILogger<WayfinderStageSurfaceController> logger,
    IAntiforgery antiforgery,
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

    [HttpPost]
    [Route(RoutePath)]
    public async Task<IActionResult> Advance()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning("Stage advance POST: antiforgery validation failed");
            return BadRequest("Invalid form submission.");
        }

        var result = await stageService.AdvanceAsync(HttpContext, Request.Form);

        // PRG: problems/resubmitted values ride in TempData for the block's own GET-side
        // partial to pick back up on the next render — same mechanism ServiceRequestStageService
        // callers have always used, just written here rather than by the service itself, since
        // TempData is a controller/framework concern, not stage-rendering logic.
        if (result.Problems.Count > 0)
        {
            TempData["ServiceRequestProblems"] = JsonSerializer.Serialize(result.Problems);
            TempData["ServiceRequestFormValues"] = JsonSerializer.Serialize(result.FormValues);
        }

        return Redirect(Url.IsLocalUrl(result.ReturnUrl) ? result.ReturnUrl : "/");
    }
}
