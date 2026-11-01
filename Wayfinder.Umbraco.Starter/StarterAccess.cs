using System.Security.Claims;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Starter;

/// <summary>
/// Who can do what on a starter site, worked out from the blueprints themselves so that a service
/// designed in conversation works the moment it is saved, whatever its queues are called. The
/// queue a blueprint's first stage sits in is the public one: anyone can start there and sees only
/// their own requests. Every other queue is staff work, open to the demo caseworker.
/// </summary>
public static class StarterAccess
{
    // ActorProfile treats an empty queue list as "any queue", so an empty answer must still name
    // a queue, one no blueprint can have.
    private const string NoQueue = "\u0000no-queue";

    public static ActorProfile Resolve(ClaimsPrincipal user, IEnumerable<ServiceBlueprint> blueprints)
    {
        var all = blueprints.ToList();
        var publicQueues = all
            .Select(PublicQueueKey)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!StarterPersonas.IsCaseworker(user))
        {
            var queues = OrNone(publicQueues);
            return new ActorProfile
            {
                VisibleQueues = queues,
                StartableQueues = queues,
                ActionableQueues = queues,
                RestrictToInstanceOwner = true,
            };
        }

        var staffQueues = all
            .SelectMany(blueprint => blueprint.Queues ?? [])
            .Where(queue => !publicQueues.Contains(queue.Key))
            .ToList();
        var staffKeys = OrNone(staffQueues.Select(queue => queue.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));

        return new ActorProfile
        {
            VisibleQueues = staffKeys,
            StartableQueues = staffKeys,
            ActionableQueues = staffKeys,
            RestrictToInstanceOwner = false,
            Capabilities = staffQueues
                .SelectMany(queue => queue.RoleGates ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            TeamIds = staffQueues
                .Select(queue => queue.OwningTeamId)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>The queue of the blueprint's first stage, where a member of the public starts.</summary>
    public static string? PublicQueueKey(ServiceBlueprint blueprint) =>
        blueprint.Stages.FirstOrDefault(stage => stage.StageKey == blueprint.InitialStage)?.QueueKey;

    private static string[] OrNone(HashSet<string> queues) =>
        queues.Count == 0 ? [NoQueue] : queues.ToArray();
}
