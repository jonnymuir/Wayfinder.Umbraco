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
/// Pickup/putback for the <c>wayfinderServiceRequestWorklist</c> Block Grid block — the
/// caseworker/backstage counterpart to <see cref="WayfinderStageSurfaceController"/>. "Review" an
/// item and its own advance posts are handled by <see cref="WayfinderStageSurfaceController"/>
/// directly (the worklist block renders the same stage UI for a picked instance, via
/// <see cref="ServiceRequestStageService"/>) — this controller only ever does pickup/putback.
/// </summary>
public class WayfinderWorklistSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    ILogger<WayfinderWorklistSurfaceController> logger,
    IAntiforgery antiforgery,
    ServiceRequestWorklistService worklistService)
    : SurfaceController(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    public const string PickupRoutePath = "/umbraco/wayfinder-worklist/pickup";
    public const string PutbackRoutePath = "/umbraco/wayfinder-worklist/putback";

    [HttpPost]
    [Route(PickupRoutePath)]
    public async Task<IActionResult> Pickup(string instanceId, string cursorId, string returnUrl)
    {
        if (!await ValidateAntiforgeryAsync())
        {
            return BadRequest("Invalid form submission.");
        }

        worklistService.Pickup(HttpContext, instanceId, cursorId);
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    [HttpPost]
    [Route(PutbackRoutePath)]
    public async Task<IActionResult> Putback(string instanceId, string cursorId, string returnUrl)
    {
        if (!await ValidateAntiforgeryAsync())
        {
            return BadRequest("Invalid form submission.");
        }

        worklistService.Putback(HttpContext, instanceId, cursorId);
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    private async Task<bool> ValidateAntiforgeryAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning("Worklist pickup/putback POST: antiforgery validation failed");
            return false;
        }
    }
}
