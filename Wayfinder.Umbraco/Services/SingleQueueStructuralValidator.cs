using Wayfinder.Models.ServiceDesign;
using Wayfinder.Engine.Abstractions;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Enforces Wayfinder.Umbraco's single-queue authoring constraint at save time — the editor's
/// own queue-picker already only ever offers one queue (see <see cref="WayfinderFrontStageQueue"/>),
/// but nothing stops a definition saved by another route (a hand-edited seed file, the AI
/// authoring surface, a future importer) from declaring extra ones the rendering pipeline could
/// never actually serve. Registered as an <see cref="IServiceBlueprintStructuralValidator"/> so
/// the shared engine toolkit stays unaware this constraint exists.
/// </summary>
/// <remarks>
/// Deliberately name-agnostic — only the count is checked, not a specific required queue key.
/// Nothing else in Wayfinder.Umbraco needs every host's one queue to share a fixed name (unlike
/// a host's own runtime access-control layer, which is free to hardcode whatever key it wants
/// its actor profile scoped to).
/// </remarks>
public sealed class SingleQueueStructuralValidator : IServiceBlueprintStructuralValidator
{
    public IEnumerable<ServiceBlueprintDiagnostic> Validate(ServiceBlueprint workflow)
    {
        var queues = workflow.Queues ?? Array.Empty<QueueDefinition>();
        if (queues.Count == 1)
        {
            yield break;
        }

        yield return new ServiceBlueprintDiagnostic(
            "WAYFINDER_SINGLE_QUEUE_ONLY",
            "queues",
            queues.Count == 0
                ? "Wayfinder.Umbraco definitions must declare exactly one queue — none were found."
                : "Wayfinder.Umbraco definitions must declare exactly one queue — found: " +
                  string.Join(", ", queues.Select(q => string.IsNullOrEmpty(q.Key) ? "(empty)" : q.Key)) + ".");
    }
}
