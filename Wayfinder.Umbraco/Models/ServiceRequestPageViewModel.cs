using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Models;

/// <summary>
/// View model for one rendered Wayfinder stage — carries everything the <c>_Stage-*.cshtml</c>
/// partials need. A plain class, not a wrapped <c>IPublishedContent</c>: it's built from a
/// <see cref="Services.ServiceRequestStageRenderResult"/> by the
/// <c>wayfinderServiceRequestStage</c> Block Grid block's own partial, and a Block Grid
/// element's content (<c>IPublishedElement</c>) isn't a real page with a URL/parent/children the
/// way <c>IPublishedContent</c> promises — nothing here ever needed those members anyway (every
/// <c>_Stage-*.cshtml</c> partial binds via a plain <c>@model</c>, reading only the explicit
/// properties below).
/// </summary>
public class ServiceRequestPageViewModel
{
    /// <summary>The workflow instance identifier used in form hidden fields and engine requests.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>The state version for optimistic concurrency — must be echoed in form submissions and compared on AdvanceAsync calls.</summary>
    public int StateVersion { get; set; }

    /// <summary>The workflow definition key configured on the Umbraco page property (e.g., "pension-application").</summary>
    public string BlueprintKey { get; set; } = string.Empty;

    /// <summary>The current page URL used as the POST-Redirect-Get (PRG) target after form submission.</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// The step type driving the partial view selection (e.g., "question", "check-answers", "confirmation", "status-timeline", "task-list").
    /// This value is not Archetype — it is the type classification from the engine's own state definition.
    /// </summary>
    public string StepType { get; set; } = string.Empty;

    /// <summary>Human-readable name for the current workflow state (e.g., "Your Details", "Check Your Answers").</summary>
    public string StateDisplayName { get; set; } = string.Empty;

    /// <summary>Organized GDS components for rendering form sections within workflow steps.</summary>
    public IReadOnlyList<ComponentRenderPayload> Components { get; set; } = Array.Empty<ComponentRenderPayload>();

    /// <summary>Available actions the user can take at this step (e.g., "continue", "submit", "back").</summary>
    public IReadOnlyList<ServiceRequestAction> AvailableActions { get; set; } = Array.Empty<ServiceRequestAction>();

    /// <summary>Validation problems from the previous POST, populated via TempData (PRG pattern).</summary>
    public IReadOnlyList<ServiceRequestProblem> Problems { get; set; } = Array.Empty<ServiceRequestProblem>();

    /// <summary>
    /// Tamper-proof nonce binding this rendered form to its server-side field definitions.
    /// Must be echoed in the form submission and validated on POST to ensure no fields were added, removed, or modified.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Human-readable display name for the workflow (e.g., "Get in Touch", "Pension Application").</summary>
    public string WorkflowDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// True when instancePolicy = "prompt" and an active instance already exists for this user.
    /// Causes the view to render the instance picker partial instead of the workflow form.
    /// </summary>
    public bool ShowInstancePicker { get; set; }

    /// <summary>True when the workflow engine returned a fatal error (definition not found, etc.).</summary>
    public bool HasError { get; set; }

    /// <summary>Human-readable error message when <see cref="HasError"/> is true (e.g., "Workflow definition 'pension-application' not found").</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Pre-filled field values to repopulate the form after a failed validation round-trip.
    /// Used in conjunction with <see cref="Problems"/> to preserve user input during PRG redirects (WCAG 3.3.1 compliance).
    /// Keys are field keys; values are the user-submitted strings.
    /// </summary>
    public IReadOnlyDictionary<string, string> FormValues { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Live calculation model JSON for this step (nullable). Present when the workflow
    /// definition declares a calculations block: contains the calculation set, input
    /// types/defaults and service-sourced values. Embedded on the page for the generic
    /// prism-live-form runtime, which re-evaluates the definitions as inputs change.
    /// </summary>
    public string? LiveModelJson { get; set; }

    /// <summary>
    /// Recommended polling interval in milliseconds for waiting step types.
    /// Only populated when <see cref="StepType"/> is <c>"waiting"</c>.
    /// Sourced from <see cref="WaitingConfig.PollIntervalMs"/> via the workflow response envelope.
    /// </summary>
    public int? PollAfterMs { get; set; }

    /// <summary>
    /// Convenient lookup of the first problem message keyed by field key, for rendering inline field-level errors.
    /// Returns a read-only dictionary where keys are field keys and values are their first validation error message.
    /// </summary>
    public IReadOnlyDictionary<string, string> FieldErrors =>
        Problems
            .Where(p => !string.IsNullOrEmpty(p.FieldKey))
            .GroupBy(p => p.FieldKey)
            .ToDictionary(g => g.Key, g => g.First().Message);

    /// <summary>All form fields across all components (for validation, nonce, etc.).</summary>
    public IReadOnlyList<FieldRenderPayload> AllFields =>
        Components
            .SelectMany(c => c.Fields ?? Array.Empty<FieldRenderPayload>())
            .ToList();

    /// <summary>
    /// True when this step renders at least one <c>file-upload</c> field — gates whether the
    /// view includes <c>prism-file-upload.js</c>, the same way <see cref="LiveModelJson"/>
    /// already gates <c>prism-live-form.js</c>. Derived purely from <see cref="AllFields"/>
    /// (already on this model), unlike <see cref="LiveModelJson"/>, which needs the controller's
    /// own involvement because it comes from a separate render-data key.
    /// </summary>
    public bool HasFileUploadField =>
        AllFields.Any(f => f.FieldType.Equals("file-upload", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the render-ready view model from a <see cref="ServiceRequestStageRenderResult"/> —
    /// the one place both <c>wayfinderServiceRequestStage.cshtml</c> and the worklist block's own
    /// "review this item" mode construct it, so <c>_WayfinderStageRender.cshtml</c> always sees
    /// the same shape regardless of which block rendered it.
    /// </summary>
    public static ServiceRequestPageViewModel FromRenderResult(ServiceRequestStageRenderResult result, string returnUrl)
    {
        var envelope = result.Envelope;

        if (envelope.ResponseState == "error")
        {
            return new ServiceRequestPageViewModel
            {
                BlueprintKey = result.BlueprintKey,
                ReturnUrl = returnUrl,
                HasError = true,
                ErrorMessage = envelope.Problems.FirstOrDefault()?.Message
                    ?? $"Could not start service request '{result.BlueprintKey}'."
            };
        }

        var vm = new ServiceRequestPageViewModel
        {
            InstanceId = envelope.InstanceId,
            StateVersion = envelope.StateVersion,
            BlueprintKey = result.BlueprintKey,
            ReturnUrl = returnUrl,
            StepType = envelope.Render?.StepType ?? string.Empty,
            StateDisplayName = envelope.Render?.StateDisplayName ?? string.Empty,
            Components = envelope.Render?.Components ?? Array.Empty<ComponentRenderPayload>(),
            AvailableActions = envelope.Render?.AvailableActions ?? Array.Empty<ServiceRequestAction>(),
            Problems = result.Problems,
            FormValues = result.FormValues,
            Nonce = result.Nonce,
            PollAfterMs = envelope.PollAfterMs,
            LiveModelJson = envelope.Render?.Data?["live"]?.ToJsonString()
        };

        if (envelope.ResponseState == "instance_picker")
        {
            vm.ShowInstancePicker = true;
            vm.StateDisplayName = envelope.Render?.StateDisplayName ?? result.BlueprintKey;
        }

        return vm;
    }
}
