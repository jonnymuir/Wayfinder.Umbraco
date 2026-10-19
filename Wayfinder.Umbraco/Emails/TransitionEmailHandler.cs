using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Emails;

/// <summary>Queues each email whose blueprint and move match a request's move, as the move happens.</summary>
public sealed class TransitionEmailHandler(
    IOptions<WayfinderEmailOptions> options,
    IServiceRequestStore requests,
    IWayfinderEmailQueue queue) : INotificationAsyncHandler<ServiceRequestTransitionedNotification>
{
    public Task HandleAsync(ServiceRequestTransitionedNotification notification, CancellationToken cancellationToken)
    {
        var move = notification.Transition;
        if (move.Action is not { } action || !requests.TryGet(move.InstanceId, out var request))
        {
            return Task.CompletedTask;
        }

        var matching = options.Value.Emails
            .Where(email => string.Equals(email.BlueprintKey, request.BlueprintKey, StringComparison.OrdinalIgnoreCase)
                && email.Actions.Contains(action, StringComparer.Ordinal))
            .ToList();
        if (matching.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Pinned now: by the time the email goes, the person may have written again.
        var lengths = request.FieldValues
            .Select(field => (field.Key, Count: ConversationEntry.ReadThread(field.Value).Count))
            .Where(field => field.Count > 0)
            .ToDictionary(field => field.Key, field => field.Count, StringComparer.Ordinal);

        foreach (var email in matching)
        {
            queue.Enqueue(new WayfinderEmailJob(email.Name, request.InstanceId, action, lengths));
        }

        return Task.CompletedTask;
    }
}
