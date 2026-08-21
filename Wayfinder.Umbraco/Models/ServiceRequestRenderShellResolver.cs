using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Models;

/// <summary>
/// Resolves the workflow render shell from the component tree, with legacy <c>stepType</c> fallback.
/// </summary>
public static class ServiceRequestRenderShellResolver
{
    public static string ResolveShell(
        IReadOnlyList<ComponentRenderPayload>? components,
        string? legacyStepType,
        bool hasWaitingConfig,
        bool hasAvailableActions)
    {
        var items = components ?? Array.Empty<ComponentRenderPayload>();

        if (hasWaitingConfig || HasComponentType(items, "waiting"))
        {
            return "waiting";
        }

        if (HasComponentType(items, "task-list"))
        {
            return "task-list";
        }

        if (AllDataCarryingComponentsAreSummaryLists(items))
        {
            return "check-answers";
        }

        var hasInteractiveInputs = items.Any(ComponentHasInteractiveInputs);

        // A panel signals a genuinely terminal/confirmation screen only when there's nothing
        // left to do here — no interactive inputs AND no real routes to trigger. The core
        // Wayfinder blueprint convention also uses "panel" purely for a heading treatment on
        // stages that still have real actions (see the reference njf-contributions.json's own
        // review/confirm-warnings stages) — the core Wayfinder.Rendering.GovUk pipeline renders
        // those fine (it has no notion of shells at all), but this package's own shell split
        // was flattening any such stage to _Stage-Completion's inert <a href="/"> links instead
        // of real submit buttons. Found live: a bulk-data-review stage's own "Resubmit corrected
        // file" route rendered as a dead link once AvailableActions was actually populated.
        if (HasComponentType(items, "panel") && !hasInteractiveInputs && !hasAvailableActions)
        {
            return "confirmation";
        }

        if (items.Count > 0 && !hasInteractiveInputs && !hasAvailableActions)
        {
            return "status-timeline";
        }

        var normalized = NormalizeShell(legacyStepType);
        if (normalized == "confirmation" && hasAvailableActions)
        {
            // The engine's own component-shape inference (ComponentExtensions.InferStepType,
            // which has no notion of AvailableActions) still said "confirmation" — right for a
            // genuinely terminal stage, wrong here since real actions exist. check-answers
            // (_Stage-Review.cshtml) renders the exact same shape with real submit buttons.
            return "check-answers";
        }

        return normalized ?? "question";
    }

    private static bool AllDataCarryingComponentsAreSummaryLists(IReadOnlyList<ComponentRenderPayload> components)
    {
        var dataCarryingTypes = components
            .Where(ComponentCarriesFieldData)
            .Select(c => NormalizeType(c.Type))
            .ToArray();

        return dataCarryingTypes.Length > 0
            && dataCarryingTypes.All(type => string.Equals(type, "summary-list", StringComparison.Ordinal));
    }

    private static bool ComponentCarriesFieldData(ComponentRenderPayload component) =>
        component.Fields.Any() || (component.AccordionSections?.Any(section => section.Fields.Any()) ?? false);

    private static bool ComponentHasInteractiveInputs(ComponentRenderPayload component)
    {
        if (component.Fields.Any(field => !IsContentOnlyFieldType(field.FieldType)))
        {
            return true;
        }

        return component.AccordionSections?.Any(section =>
            section.Fields.Any(field => !IsContentOnlyFieldType(field.FieldType))) ?? false;
    }

    private static bool IsContentOnlyFieldType(string? fieldType) => NormalizeType(fieldType) switch
    {
        "inset-text" or "warning-text" or "details" or "notification-banner" or "body" or "heading" => true,
        _ => false
    };

    private static bool HasComponentType(IReadOnlyList<ComponentRenderPayload> components, string expectedType) =>
        components.Any(component => string.Equals(
            NormalizeType(component.Type),
            expectedType,
            StringComparison.Ordinal));

    private static string? NormalizeShell(string? stepType) => NormalizeType(stepType) switch
    {
        "collect" => "question",
        "review" => "check-answers",
        "completion" => "confirmation",
        "statustimeline" => "status-timeline",
        "tasklist" => "task-list",
        "" => null,
        var normalized => normalized
    };

    private static string NormalizeType(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
