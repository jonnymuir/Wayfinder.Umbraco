using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Models;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// Umbraco route-hijacking controller for the <c>serviceRequestHub</c> document type — "My
/// Service Requests" for the authenticated actor, listing every instance
/// <see cref="IProcessManager.GetInstances"/> returns for them. The engine is authoritative and
/// in-process (<see cref="UmbracoProcessManagerEngine"/>) — there is no longer a second, remote
/// "Business App" source to merge in.
/// </summary>
public class ServiceRequestHubController(
    ILogger<ServiceRequestHubController> logger,
    ICompositeViewEngine compositeViewEngine,
    IUmbracoContextAccessor umbracoContextAccessor,
    IProcessManager processManager,
    IOptions<WayfinderServiceDesignOptions> optionsAccessor,
    IPublishedValueFallback publishedValueFallback)
    : RenderController(logger, compositeViewEngine, umbracoContextAccessor)
{
    public override IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Redirect(BuildLoginRedirectUrl());
        }

        return IndexInternal();
    }

    private IActionResult IndexInternal()
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(HttpContext);
        var userId = options.ResolveUserId(HttpContext);

        var allInstances = processManager.GetInstances(tenantId, userId).Instances
            .OrderByDescending(i => i.LastUpdatedAt)
            .ToList();

        var activeInstances = allInstances
            .Where(i => !i.IsCompleted)
            .Select(i => new ServiceRequestViewModel { Summary = i, ResumeUrl = ResolveStagePageUrl(i) })
            .ToList();

        var completedInstances = allInstances
            .Where(i => i.IsCompleted)
            .Select(i => new ServiceRequestViewModel { Summary = i, ResumeUrl = ResolveStagePageUrl(i) })
            .ToList();

        var vm = new ServiceRequestHubViewModel(CurrentPage!, publishedValueFallback)
        {
            ActiveInstances = activeInstances,
            CompletedInstances = completedInstances
        };

        return CurrentTemplate(vm);
    }

    /// <summary>
    /// A Block Grid-composed page has no fixed content-type identity to search for the way the
    /// old single-purpose <c>stagePage</c> document type did — resolution here is deliberately
    /// just <see cref="ServiceRequestSummary.ServiceRequestPageUrl"/> (the engine's own record of
    /// where a blueprint's stage lives, when it has one) with a hub-page fallback, not a
    /// content-tree search.
    /// </summary>
    private string ResolveStagePageUrl(ServiceRequestSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(summary.ServiceRequestPageUrl) && Url.IsLocalUrl(summary.ServiceRequestPageUrl))
        {
            if (!summary.IsCompleted && !string.IsNullOrWhiteSpace(summary.InstanceId))
            {
                var separator = summary.ServiceRequestPageUrl.Contains('?') ? "&" : "?";
                return $"{summary.ServiceRequestPageUrl}{separator}instanceId={Uri.EscapeDataString(summary.InstanceId)}";
            }
            return summary.ServiceRequestPageUrl;
        }

        return CurrentPage?.Url() ?? "/";
    }

    private string BuildLoginRedirectUrl()
    {
        var returnUrl = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
        return $"/auth/login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
