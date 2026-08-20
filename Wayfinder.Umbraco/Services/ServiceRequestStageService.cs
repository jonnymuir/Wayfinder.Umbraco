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

    public async Task<ServiceRequestStageRenderResult> RenderCurrentAsync(
        HttpContext ctx,
        string blueprintKey,
        string? instanceId,
        string? action,
        IReadOnlyList<ServiceRequestProblem>? problems = null,
        IReadOnlyDictionary<string, string>? formValues = null)
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

        // Check-answers is a read-only summary — it has no fields to validate on POST.
        var stepType = envelope.Render?.StepType ?? string.Empty;
        var nonceFields = stepType == "check-answers"
            ? []
            : envelope.Render?.Components.SelectMany(c => c.Fields).ToList() ?? [];

        var nonce = await nonceService.CreateAsync(nonceFields);

        return new ServiceRequestStageRenderResult(envelope, blueprintKey, nonce, problems ?? [], formValues ?? new Dictionary<string, string>());
    }

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

        var authoritativeFields = await nonceService.ResolveAsync(nonce);
        if (authoritativeFields == null)
        {
            logger.LogWarning("Stage advance: nonce expired or invalid — redirecting to GET");
            return ServiceRequestStageAdvanceResult.Redirect(returnUrl);
        }

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
