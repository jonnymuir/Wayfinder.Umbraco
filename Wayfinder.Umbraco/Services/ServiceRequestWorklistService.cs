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
/// Deliberately doesn't depend on the core repo's <c>Wayfinder.Engine.Worklist</c> package — that
/// package's minimal-API + raw-HTML-string shape doesn't fit an Umbraco-hosted, Block
/// Grid-composed page; only the underlying <see cref="IProcessManager"/> calls are shared.
/// </summary>
public class ServiceRequestWorklistService(
    IProcessManager processManager,
    IOptions<WayfinderServiceDesignOptions> optionsAccessor)
{
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

    public ServiceRequestResponseEnvelope Pickup(HttpContext ctx, string instanceId, string cursorId)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        return processManager.PickupWorkItem(instanceId, cursorId, tenantId, userId, accessProfile);
    }

    public ServiceRequestResponseEnvelope Putback(HttpContext ctx, string instanceId, string cursorId)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        return processManager.PutbackWorkItem(instanceId, cursorId, tenantId, userId, accessProfile);
    }
}
