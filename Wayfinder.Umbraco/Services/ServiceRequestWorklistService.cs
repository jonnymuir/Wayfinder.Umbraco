using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// The query/pickup/putback logic the <c>wayfinderServiceRequestWorklist</c> Block Grid block
/// calls into — the caseworker/backstage counterpart to <see cref="ServiceRequestStageService"/>.
/// Calls <see cref="IProcessManager.GetQueueWorkItems"/>/<see cref="IProcessManager.PickupWorkItem"/>/
/// <see cref="IProcessManager.PutbackWorkItem"/> directly (the engine is authoritative and
/// in-process), resolving identity the same way <see cref="ServiceRequestStageService"/> does.
/// The Block Grid partial renders this data via <c>Wayfinder.Engine.Worklist</c>'s own
/// <c>WorklistRenderer</c> — the GOV.UK markup is shared with every other worklist host; only
/// query/pickup/putback wiring against a Block Grid page's own identity/routing lives here.
/// </summary>
public class ServiceRequestWorklistService(
    IProcessManager processManager,
    IOptions<WayfinderServiceDesignOptions> optionsAccessor)
{
    /// <summary>
    /// Lists the work items the calling caseworker's <c>ActorProfile</c> can see across whatever
    /// queues it has visibility into — the caseworker worklist's own paged/filtered/sorted query,
    /// resolving identity via <see cref="WayfinderServiceDesignOptions"/> the same way every other
    /// method on this class does.
    /// </summary>
    public QueueWorkListEnvelope GetWorklist(
        HttpContext ctx,
        IReadOnlyCollection<QueueWorkItemStatus>? statuses = null,
        QueueWorkListSort sort = QueueWorkListSort.Default,
        string? searchText = null,
        int pageIndex = 0,
        int pageSize = 20)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        return processManager.GetQueueWorkItems(tenantId, userId, accessProfile, statuses, sort, searchText, pageIndex, pageSize);
    }

    /// <summary>Claims a shared queue's work item cursor for the calling caseworker, so nobody else can act on it until they put it back.</summary>
    public ServiceRequestResponseEnvelope Pickup(HttpContext ctx, string instanceId, string cursorId)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        return processManager.PickupWorkItem(instanceId, cursorId, tenantId, userId, accessProfile);
    }

    /// <summary>Releases a work item cursor the calling caseworker previously picked up, returning it to the shared queue for anyone eligible to claim.</summary>
    public ServiceRequestResponseEnvelope Putback(HttpContext ctx, string instanceId, string cursorId)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        return processManager.PutbackWorkItem(instanceId, cursorId, tenantId, userId, accessProfile);
    }
}
