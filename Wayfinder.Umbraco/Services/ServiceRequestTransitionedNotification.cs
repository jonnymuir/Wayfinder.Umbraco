using Umbraco.Cms.Core.Notifications;
using Wayfinder.Engine.Abstractions;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// A service request has moved from one stage to another, and the move is saved: published once per
/// transition the engine records, whatever caused it (a submitted stage, a caseworker's action from the
/// worklist, a gateway, a support system's outcome). Handle it to react to a move, sending an email
/// when a request reaches a particular stage, say, with
/// <c>builder.AddNotificationAsyncHandler&lt;ServiceRequestTransitionedNotification, YourHandler&gt;()</c>.
/// It is published during the request that made the move: keep a handler quick and queue anything
/// slow. A handler that throws is logged and never undoes the move.
/// </summary>
/// <param name="transition">The recorded transition: the request, who moved it, from and to which stage, and by which action.</param>
public sealed class ServiceRequestTransitionedNotification(AuditEvent transition) : INotification
{
    public AuditEvent Transition { get; } = transition;
}
