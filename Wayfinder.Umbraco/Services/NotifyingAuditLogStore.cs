using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Wayfinder.Engine.Abstractions;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Wraps the audit store the engine writes to so that every recorded transition is also published as
/// a <see cref="ServiceRequestTransitionedNotification"/>. The engine records a transition only once its
/// save has landed, which makes this the one place that sees every completed move, including a
/// caseworker's move into a queue they can no longer see.
/// </summary>
internal sealed class NotifyingAuditLogStore(IAuditLogStore inner, IEventAggregator eventAggregator, ILogger logger) : IAuditLogStore
{
    public void Record(AuditEvent auditEvent)
    {
        inner.Record(auditEvent);
        if (auditEvent.EventType != AuditEventType.Transition)
        {
            return;
        }

        try
        {
            eventAggregator.Publish(new ServiceRequestTransitionedNotification(auditEvent));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A ServiceRequestTransitionedNotification handler failed for instance {InstanceId}; the move stands.", auditEvent.InstanceId);
        }
    }

    public IReadOnlyList<AuditEvent> GetByInstance(string instanceId) => inner.GetByInstance(instanceId);

    public IReadOnlyList<AuditEvent> Query(
        string? instanceId = null,
        string? actor = null,
        AuditEventSeverity? minimumSeverity = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int pageIndex = 0,
        int pageSize = 50) =>
        inner.Query(instanceId, actor, minimumSeverity, fromUtc, toUtc, pageIndex, pageSize);
}
