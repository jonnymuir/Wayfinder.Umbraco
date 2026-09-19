using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// Backoffice-hosted cross-tenant admin search and soft-abort for stuck instances — the same
/// <see cref="IProcessManager.SearchInstancesForAdmin"/>/<see cref="IProcessManager.AbortInstance"/>
/// surface built to diagnose a real production instance permanently parked at a join gateway
/// (see Umbraco.Prism #284/#285's own history). Reuses <see cref="WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin"/>
/// rather than introducing a second admin tier — the same trusted backoffice group already
/// entrusted with blueprint design is entrusted with stopping a stuck citizen's instance too.
/// </summary>
[Authorize(Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin)]
[VersionedApiBackOfficeRoute("wayfinder")]
[ApiExplorerSettings(GroupName = "Wayfinder")]
[MapToApi("Wayfinder")]
public class ServiceRequestAdminController(
    IProcessManager processManager,
    IBackOfficeSecurityAccessor backOfficeSecurityAccessor) : ManagementApiControllerBase
{
    /// <summary>The backoffice list/search view's own data source.</summary>
    [HttpGet("service-requests")]
    public IActionResult SearchServiceRequests(
        [FromQuery] string? blueprintKey,
        [FromQuery] string? tenantId,
        [FromQuery] bool includeAborted = false,
        [FromQuery] string? searchText = null,
        [FromQuery] ServiceRequestAdminSort sort = ServiceRequestAdminSort.UpdatedAtOldestFirst,
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 20)
    {
        var result = processManager.SearchInstancesForAdmin(new ServiceRequestAdminQuery
        {
            BlueprintKey = blueprintKey,
            TenantId = tenantId,
            IncludeAborted = includeAborted,
            SearchText = searchText,
            Sort = sort,
            PageIndex = pageIndex,
            PageSize = pageSize
        });

        return Ok(result);
    }

    /// <summary>
    /// Soft-terminates a stuck instance. The acting admin's own backoffice user id is resolved
    /// server-side for the audit trail, never trusted from the request body.
    /// </summary>
    [HttpPost("service-requests/{instanceId}/abort")]
    public IActionResult AbortServiceRequest(string instanceId, [FromBody] AbortServiceRequestRequest request)
    {
        var abortedByUserId = backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Username ?? "unknown";
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Stopped via the backoffice admin screen."
            : request.Reason;

        var aborted = processManager.AbortInstance(instanceId, reason, abortedByUserId);
        return aborted ? Ok() : NotFound();
    }
}

/// <summary>Request body for <see cref="ServiceRequestAdminController.AbortServiceRequest"/>.</summary>
public sealed record AbortServiceRequestRequest(string? Reason);
