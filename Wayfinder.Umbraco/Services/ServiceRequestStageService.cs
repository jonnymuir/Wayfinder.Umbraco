using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Services.Validation;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// The render/advance logic every Wayfinder-Umbraco stage surface needs — extracted so both the
/// <c>wayfinderServiceRequestStage</c> Block Grid partial (GET/render) and
/// <see cref="Controllers.WayfinderStageSurfaceController"/> (POST/advance) share exactly one
/// implementation, instead of each re-deriving it. Calls <see cref="IProcessManager"/> directly —
/// this package's engine is authoritative and in-process
/// (<see cref="UmbracoProcessManagerEngine"/>), resolving identity via
/// <see cref="WayfinderServiceDesignOptions"/> rather than forwarding to a remote "Business App".
/// </summary>
public class ServiceRequestStageService(
    IProcessManager processManager,
    IOptions<WayfinderServiceDesignOptions> optionsAccessor,
    IStageNonceService nonceService,
    IServiceRequestFileStorage fileStorage,
    IUploadTokenService uploadTokenService,
    ILogger<ServiceRequestStageService> logger)
{
    private const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;
    private const string FieldPrefix = "field:";

    /// <summary>
    /// Renders the current (or a specific) stage of a citizen's service request — the GET-side
    /// entry point both the <c>wayfinderServiceRequestStage</c> Block Grid partial and any custom
    /// host surface call. Resolves the caller's tenant/user/<c>ActorProfile</c> from
    /// <see cref="WayfinderServiceDesignOptions"/>, asks the engine for the current envelope, mints
    /// a fresh nonce bound to this instance/user for the eventual whole-page submission, and never
    /// throws — an unexpected failure comes back as the same <c>ResponseState == "error"</c> shape
    /// the engine's own expected failures use (see this method's own <c>catch</c> for why).
    /// </summary>
    /// <param name="ctx">The current request, used to resolve identity and build stage-relative URLs.</param>
    /// <param name="blueprintKey">Which service blueprint to render.</param>
    /// <param name="instanceId">A specific instance to resume, or <see langword="null"/> to resolve the caller's current/latest one.</param>
    /// <param name="action">An explicit transition to take on load (rare — most stages render via a plain GET).</param>
    /// <param name="problems">Validation problems carried over from a failed POST (PRG pattern), if any.</param>
    /// <param name="formValues">Submitted values to repopulate the form with, alongside <paramref name="problems"/>.</param>
    public async Task<ServiceRequestStageRenderResult> RenderCurrentAsync(
        HttpContext ctx,
        string blueprintKey,
        string? instanceId,
        string? action,
        IReadOnlyList<ServiceRequestProblem>? problems = null,
        IReadOnlyDictionary<string, string>? formValues = null)
    {
        try
        {
            return await RenderCurrentCoreAsync(ctx, blueprintKey, instanceId, action, problems, formValues);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or StackOverflowException or OutOfMemoryException))
        {
            // A stage page is citizen-facing GOV.UK content — an unhandled exception here (a
            // corrupt stored instance, a calculation error, a null-resolver bug) must never
            // surface as a raw ASP.NET Core error page. Render it through the exact same
            // ResponseState == "error" envelope shape GetCurrent/Advance already produce for an
            // *expected* failure, so the Block Grid partial has exactly one error-rendering path
            // regardless of which kind of failure it was.
            logger.LogError(ex, "Unhandled exception rendering stage {BlueprintKey}/{InstanceId}", blueprintKey, instanceId);
            var errorEnvelope = new ServiceRequestResponseEnvelope
            {
                InstanceId = instanceId ?? string.Empty,
                ResponseState = "error",
                StateVersion = 0,
                CorrelationId = Guid.NewGuid().ToString(),
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Problems = [new ServiceRequestProblem
                {
                    FieldKey = string.Empty,
                    Message = "Sorry, there is a problem with this service. Try again later.",
                    Code = "UNHANDLED_EXCEPTION",
                }],
            };
            return new ServiceRequestStageRenderResult(
                errorEnvelope, blueprintKey, Nonce: "", problems ?? [], formValues ?? new Dictionary<string, string>());
        }
    }

    private async Task<ServiceRequestStageRenderResult> RenderCurrentCoreAsync(
        HttpContext ctx,
        string blueprintKey,
        string? instanceId,
        string? action,
        IReadOnlyList<ServiceRequestProblem>? problems,
        IReadOnlyDictionary<string, string>? formValues)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        var envelope = processManager.GetCurrent(
            blueprintKey, tenantId, userId, accessProfile,
            string.IsNullOrEmpty(instanceId) ? null : instanceId,
            string.IsNullOrEmpty(action) ? null : action);

        if (envelope.ResponseState is "error" or "instance_picker")
        {
            return new ServiceRequestStageRenderResult(envelope, blueprintKey, Nonce: "", problems ?? [], formValues ?? new Dictionary<string, string>());
        }

        // A caseworker/citizen viewing an uploaded file, or a bulk-data-review component's own
        // row cards, needs real REST URLs to fetch against — see WayfinderStageDataController,
        // the routes these prefixes resolve to. Without this, WithBulkDatasetApiUrls never fires
        // (ComponentRenderPayload.BulkDatasetApiUrl stays null), and the client-side bulk review
        // script never even runs: the component keeps rendering its own static "Nothing to review
        // yet" placeholder regardless of how much data was actually ingested. Found live via a
        // real Playwright walkthrough that submitted a genuine file and got back a correct summary
        // but no row cards to act on.
        var (filesPrefix, bulkDatasetsPrefix) = Controllers.WayfinderStageDataController.BuildUrlPrefixes(blueprintKey, envelope.InstanceId);
        envelope = envelope.WithFileDownloadUrls(filesPrefix);
        envelope = envelope.WithBulkDatasetApiUrls(bulkDatasetsPrefix);

        // Always the real rendered fields, regardless of StepType. A prior version special-cased
        // "check-answers" to an empty list (the reasoning: a check-answers page is a read-only
        // summary, nothing to validate on POST) — wrong, because ComponentExtensions.InferStepType
        // classifies a stage as "check-answers" the moment it contains ANY SummaryListComponent
        // anywhere in its tree, even one that's just a caseworker's note alongside genuinely new,
        // required input fields (the "request more information" reject-resubmit pattern). That
        // shortcut silently emptied the nonce's field list for such a stage, so every real
        // submitted field came back "Unknown field" on POST — confirmed live via a real HTTP
        // round-trip against a blueprint using exactly this pattern. The special case was also
        // redundant even for a genuinely pure check-answers stage: its own real fields are already
        // all ReadOnly (BuildComponents stamps summary-list children ReadOnly = true), which
        // FieldValueValidator already skips — so just always computing the real list is both
        // correct and no more expensive for the pure case.
        var nonceFields = envelope.Render?.Components.SelectMany(c => c.Fields).ToList() ?? [];

        var nonce = await nonceService.CreateAsync(envelope.InstanceId, userId, nonceFields);

        return new ServiceRequestStageRenderResult(envelope, blueprintKey, nonce, problems ?? [], formValues ?? new Dictionary<string, string>());
    }

    /// <summary>
    /// Handles a stage's whole-page form POST — validates the presented nonce against the caller's
    /// instance/user (see <see cref="IStageNonceService"/>), resolves any async-uploaded files by
    /// their token, submits the merged field values to the engine, and always returns a
    /// PRG-pattern redirect back to <see cref="ServiceRequestStageAdvanceResult.ReturnUrl"/> —
    /// validation problems and resubmitted values ride in <c>TempData</c> for the next GET to pick
    /// back up, rather than rendering the result of a POST directly.
    /// </summary>
    /// <param name="ctx">The current request, used to resolve identity.</param>
    /// <param name="form">The submitted form — <c>ReturnUrl</c>/<c>InstanceId</c>/<c>Nonce</c> plus every <c>field:{fieldKey}</c> value.</param>
    public async Task<ServiceRequestStageAdvanceResult> AdvanceAsync(HttpContext ctx, IFormCollection form)
    {
        var options = optionsAccessor.Value;
        var tenantId = options.ResolveTenantId!(ctx);
        var userId = options.ResolveUserId(ctx);
        var accessProfile = options.ResolveAccessProfile!(ctx);

        var returnUrl = form["ReturnUrl"].ToString();
        var instanceId = form["InstanceId"].ToString();
        var nonce = form["Nonce"].ToString();

        if (string.IsNullOrEmpty(nonce))
        {
            logger.LogWarning("Stage advance: missing nonce — possible form tampering");
            return ServiceRequestStageAdvanceResult.Redirect(returnUrl);
        }

        var authoritativeFields = await nonceService.ResolveAsync(nonce, instanceId, userId);
        if (authoritativeFields == null)
        {
            logger.LogWarning("Stage advance: nonce expired, invalid, or bound to a different instance/user — redirecting to GET");
            return ServiceRequestStageAdvanceResult.Redirect(returnUrl);
        }

        // Consumed the moment a whole-page submission actually uses it — bounds replay of this
        // exact submission to "before this point", rather than "until the nonce's own TTL
        // expires" (an async file-upload's own resolve never reaches here, so it's unaffected).
        await nonceService.InvalidateAsync(nonce);

        // Fields post under GovUk.FieldName's "field:{fieldKey}" convention (Wayfinder.Rendering.GovUk's own rendering contract).
        var submittedFields = form.Keys
            .Where(k => k.StartsWith(FieldPrefix, StringComparison.Ordinal))
            .ToDictionary(k => k[FieldPrefix.Length..], k => form[k].ToString());

        // Files never appear in form.Keys — a file-upload field's "value" for required-checking
        // purposes is simply whether a file was posted for it.
        var postedFiles = authoritativeFields
            .Where(field => field.FieldType.Equals("file-upload", StringComparison.OrdinalIgnoreCase))
            .Select(field => (Field: field, File: form.Files.GetFile(GovUk.FieldName(field.FieldKey))))
            .Where(pair => pair.File is not null)
            .ToList();

        // A file-upload field with no raw posted file may instead carry the opaque token an
        // async-upload endpoint issued when the visitor's browser uploaded it ahead of this
        // submission — resolve those here. A token is only trusted if it resolves at all AND
        // its cached binding names this exact instance/field.
        var postedFileKeys = postedFiles.Select(pair => pair.Field.FieldKey).ToHashSet(StringComparer.Ordinal);
        var tokenUploads = new List<(FieldRenderPayload Field, UploadTokenBinding Binding)>();
        var validationOverrides = new Dictionary<string, string>();
        foreach (var field in authoritativeFields.Where(f =>
            f.FieldType.Equals("file-upload", StringComparison.OrdinalIgnoreCase) && !postedFileKeys.Contains(f.FieldKey)))
        {
            if (!submittedFields.TryGetValue(field.FieldKey, out var token) || string.IsNullOrWhiteSpace(token))
            {
                // Nothing submitted for this field this round — preserve whatever's already
                // stored (the same value the "Uploaded: …" display state renders from) by
                // leaving it out of fieldValues entirely, letting the engine's own merge keep it.
                if (!string.IsNullOrWhiteSpace(field.Value?.ToString()))
                {
                    validationOverrides[field.FieldKey] = field.Value!.ToString()!;
                }
                continue;
            }

            var binding = await uploadTokenService.ResolveAsync(token);
            if (binding is not null
                && string.Equals(binding.InstanceId, instanceId, StringComparison.Ordinal)
                && string.Equals(binding.FieldKey, field.FieldKey, StringComparison.Ordinal))
            {
                tokenUploads.Add((field, binding));
            }
            else
            {
                submittedFields[field.FieldKey] = string.Empty;
            }
        }

        var validationInput = new Dictionary<string, string>(submittedFields);
        foreach (var (field, _) in postedFiles)
        {
            validationInput[field.FieldKey] = "uploaded";
        }
        foreach (var (fieldKey, value) in validationOverrides)
        {
            validationInput[fieldKey] = value;
        }

        var validationResult = FieldValueValidator.Validate(authoritativeFields, validationInput);
        var errors = new Dictionary<string, string>(validationResult.Errors);

        foreach (var (field, file) in postedFiles)
        {
            var maxSizeBytes = field.MaxSizeBytes ?? DefaultMaxFileSizeBytes;
            if (file!.Length > maxSizeBytes)
            {
                errors[field.FieldKey] = $"{field.Label} must be smaller than {maxSizeBytes / (1024 * 1024)}MB.";
            }
        }

        if (errors.Count > 0)
        {
            var problems = errors
                .Select(e => new ServiceRequestProblem { FieldKey = e.Key, Message = e.Value, Code = "validation_error" })
                .ToList();
            return ServiceRequestStageAdvanceResult.Redirect(returnUrl, problems, submittedFields);
        }

        var blueprintKey = form["BlueprintKey"].ToString();
        var action = form["Action"].ToString();
        var stateVersion = int.TryParse(form["StateVersion"], out var sv) ? sv : 0;

        if (string.IsNullOrEmpty(instanceId) || !form.ContainsKey("Action"))
        {
            logger.LogWarning("Stage advance: missing InstanceId or Action");
            return ServiceRequestStageAdvanceResult.Redirect(returnUrl);
        }

        var fieldValues = submittedFields.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);

        // An untouched file-upload field still posts as a regular, empty form field — strip it
        // back out so the engine sees a genuinely omitted key instead of an explicit "" (which
        // it would read as "cleared").
        foreach (var fieldKey in validationOverrides.Keys)
        {
            fieldValues.Remove(fieldKey);
        }

        foreach (var (field, file) in postedFiles)
        {
            fieldValues[field.FieldKey] = await fileStorage.SaveAsync(instanceId, field.FieldKey, file!);
        }

        foreach (var (field, binding) in tokenUploads)
        {
            fieldValues[field.FieldKey] = binding.Reference;
        }

        // Combine GDS date sub-input parts (-day/-month/-year) into a display value.
        foreach (var field in authoritativeFields.Where(f => f.FieldType.Equals("date", StringComparison.OrdinalIgnoreCase)))
        {
            if (fieldValues.TryGetValue($"{field.FieldKey}-day", out var day) &&
                fieldValues.TryGetValue($"{field.FieldKey}-month", out var month) &&
                fieldValues.TryGetValue($"{field.FieldKey}-year", out var year) &&
                !string.IsNullOrWhiteSpace(day?.ToString()) &&
                !string.IsNullOrWhiteSpace(month?.ToString()) &&
                !string.IsNullOrWhiteSpace(year?.ToString()))
            {
                fieldValues[field.FieldKey] = $"{day}/{month}/{year}";
            }
        }

        var envelope = processManager.Advance(instanceId, tenantId, userId, accessProfile, action, stateVersion, fieldValues);

        if (envelope.Problems.Count > 0)
        {
            return ServiceRequestStageAdvanceResult.Redirect(returnUrl, envelope.Problems, submittedFields);
        }

        return ServiceRequestStageAdvanceResult.Redirect(returnUrl);
    }
}

/// <summary>The render-path result — everything a Block Grid partial needs to render one stage.</summary>
public record ServiceRequestStageRenderResult(
    ServiceRequestResponseEnvelope Envelope,
    string BlueprintKey,
    string Nonce,
    IReadOnlyList<ServiceRequestProblem> Problems,
    IReadOnlyDictionary<string, string> FormValues);

/// <summary>
/// The advance-path result — always a PRG redirect back to <see cref="ReturnUrl"/>, carrying
/// validation/engine problems and resubmitted form values via <see cref="Problems"/>/
/// <see cref="FormValues"/> for the controller to stash in TempData (WCAG 3.3.1: don't silently
/// discard what the visitor just typed).
/// </summary>
public record ServiceRequestStageAdvanceResult(
    string ReturnUrl,
    IReadOnlyList<ServiceRequestProblem> Problems,
    IReadOnlyDictionary<string, string> FormValues)
{
    public static ServiceRequestStageAdvanceResult Redirect(
        string? returnUrl,
        IReadOnlyList<ServiceRequestProblem>? problems = null,
        IReadOnlyDictionary<string, string>? formValues = null) =>
        new(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl, problems ?? [], formValues ?? new Dictionary<string, string>());
}
