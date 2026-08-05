using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Models;
using Wayfinder.Umbraco.Services;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;

namespace Wayfinder.Umbraco.TagHelpers;

/// <summary>
/// Renders a service blueprint component (container) or field (input). A host's own Razor
/// override (see <see cref="ComponentPartialResolver"/> for exactly how, and where) always wins;
/// everything else falls through to <c>Wayfinder.Rendering.GovUk</c>'s <see cref="GovUkComponentRenderer"/>
/// — the shared package's own built-in catalog, the gold-standard rendering for every type
/// including slider/stat-group/chart. <c>file-upload</c> is the one permanent exception (see
/// below), handled directly by this tag helper rather than the renderer.
/// </summary>
/// <remarks>
/// <para>
/// Container usage: &lt;wayfinder-component component="@comp" errors="@Model.FieldErrors" values="@Model.FormValues"
///   return-url="@Model.ReturnUrl" instance-id="@Model.InstanceId" state-version="@Model.StateVersion"
///   blueprint-key="@Model.BlueprintKey" nonce="@Model.Nonce" /&gt;
/// </para>
/// <para>
/// Input field usage: &lt;wayfinder-field field="@field" errors="@Model.FieldErrors" values="@Model.FormValues" /&gt;
/// — the standard way a container partial (fieldset, accordion) renders its own fields.
/// </para>
/// <para>
/// To override rendering for a component with Type = "fieldset", place
/// ~/Views/Partials/Components/_Component-Fieldset.cshtml in your own app. To override a field
/// with FieldType = "text", place ~/Views/Partials/Fields/_Component-Text.cshtml. Either folder's
/// own _Component-Default.cshtml can also be overridden as the catch-all fallback. This host-facing
/// contract is unchanged from before this package adopted Wayfinder.Rendering.GovUk.
/// </para>
/// <para>
/// Type normalisation: kebab-case is converted to PascalCase.
/// "summary-list" → "SummaryList", "notification-banner" → "NotificationBanner".
/// </para>
/// </remarks>
[HtmlTargetElement("wayfinder-component")]
[HtmlTargetElement("wayfinder-field")]
public class ComponentTagHelper : TagHelper
{
    private readonly IHtmlHelper _htmlHelper;
    private readonly ComponentPartialResolver _partialResolver;
    private readonly GovUkComponentRenderer _renderer;
    private readonly string _fileEndpointBasePath;

    public ComponentTagHelper(
        IHtmlHelper htmlHelper,
        ComponentPartialResolver partialResolver,
        GovUkComponentRenderer renderer,
        IOptions<WayfinderServiceDesignOptions> options)
    {
        _htmlHelper = htmlHelper;
        _partialResolver = partialResolver;
        _renderer = renderer;
        _fileEndpointBasePath = options.Value.FileEndpointBasePath;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    [HtmlAttributeName("component")]
    public ComponentRenderPayload? Component { get; set; }

    [HtmlAttributeName("field")]
    public FieldRenderPayload? Field { get; set; }

    [HtmlAttributeName("errors")]
    public IReadOnlyDictionary<string, string>? Errors { get; set; }

    [HtmlAttributeName("values")]
    public IReadOnlyDictionary<string, string>? Values { get; set; }

    [HtmlAttributeName("return-url")]
    public string ReturnUrl { get; set; } = string.Empty;

    [HtmlAttributeName("instance-id")]
    public string InstanceId { get; set; } = string.Empty;

    [HtmlAttributeName("state-version")]
    public int StateVersion { get; set; }

    [HtmlAttributeName("blueprint-key")]
    public string BlueprintKey { get; set; } = string.Empty;

    [HtmlAttributeName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (Field is not null)
        {
            await ProcessFieldAsync(output);
            return;
        }

        if (Component is null)
        {
            output.SuppressOutput();
            return;
        }

        var hostOverride = _partialResolver.ResolveComponentHostOverride(Component.Type);
        string inner;
        if (hostOverride is not null)
        {
            ((IViewContextAware)_htmlHelper).Contextualize(ViewContext);
            var ctx = new ComponentContext
            {
                Component = Component,
                Errors = Errors ?? new Dictionary<string, string>(),
                Values = Values ?? new Dictionary<string, string>(),
                ReturnUrl = ReturnUrl,
                InstanceId = InstanceId,
                StateVersion = StateVersion,
                BlueprintKey = BlueprintKey,
                Nonce = Nonce,
                FileEndpointBasePath = _fileEndpointBasePath
            };
            inner = HtmlContentToString(await _htmlHelper.PartialAsync(hostOverride, ctx));

            // GovUkComponentRenderer.RenderComponent already wraps its own output in the
            // showWhen/Hidden div internally — a host's own Razor partial doesn't know about
            // that at all, so this tag helper has to apply the exact same wrapping itself,
            // uniformly, regardless of which path actually rendered the inner content.
            output.Content.SetHtmlContent(WrapShowWhen(Component, inner));
            return;
        }

        // Every nested field inside this component needs the same submitted-value overlay
        // ProcessFieldAsync applies for a directly-rendered field — GovUkComponentRenderer only
        // ever sees FieldRenderPayload.Value, so the overlay has to happen before it's called,
        // not inside it.
        var errors = Errors ?? new Dictionary<string, string>();
        var overlaid = Component with
        {
            Fields = Component.Fields.Select(f => f with { Value = FieldContext.ResolveDisplayValue(f, Values) }).ToArray()
        };
        output.Content.SetHtmlContent(_renderer.RenderComponent(overlaid, errors));
    }

    private static string HtmlContentToString(Microsoft.AspNetCore.Html.IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    private static string WrapShowWhen(ComponentRenderPayload component, string inner)
    {
        if (string.IsNullOrEmpty(component.ShowWhen))
        {
            return inner;
        }

        var expression = System.Net.WebUtility.HtmlEncode(component.ShowWhen);
        var hidden = component.Hidden ? " hidden" : string.Empty;
        return $"""<div data-wayfinder-show-when="{expression}"{hidden}>{inner}</div>""";
    }

    private async Task ProcessFieldAsync(TagHelperOutput output)
    {
        var fieldType = (Field!.FieldType ?? "text").ToLowerInvariant();
        var hostOverride = _partialResolver.ResolveFieldHostOverride(fieldType);

        if (hostOverride is not null)
        {
            ((IViewContextAware)_htmlHelper).Contextualize(ViewContext);
            var fieldError = Errors?.GetValueOrDefault(Field.FieldKey);
            var ctx = FieldContext.Build(Field, fieldError, Values, InstanceId, Nonce, BlueprintKey, _fileEndpointBasePath);
            output.Content.SetHtmlContent(await _htmlHelper.PartialAsync(hostOverride, ctx));
            return;
        }

        // file-upload's async progressive-upload markup needs InstanceId/Nonce/FileEndpointBasePath
        // to build its own upload/download URLs — per-request context the shared renderer's plain
        // (payload, errors) delegate signature has no room to carry, so this stays a permanent
        // special case here rather than a registered GovUkComponentRenderer override.
        if (fieldType == "file-upload")
        {
            var fieldError = Errors?.GetValueOrDefault(Field.FieldKey);
            var ctx = FieldContext.Build(Field, fieldError, Values, InstanceId, Nonce, BlueprintKey, _fileEndpointBasePath);
            output.Content.SetHtmlContent(RenderFileUpload(ctx));
            return;
        }

        var displayValue = FieldContext.ResolveDisplayValue(Field, Values);
        var errors = Errors ?? new Dictionary<string, string>();
        output.Content.SetHtmlContent(_renderer.RenderField(Field with { Value = displayValue }, errors));
    }

    // Kept in sync with ServiceRequestPageController.DefaultMaxFileSizeBytes by hand (10MB) —
    // same tradeoff as that controller's own local copy of this constant; a TagHelper
    // referencing a Controller class just to share one constant isn't a cross-layer dependency
    // worth introducing.
    private const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

    private string RenderFileUpload(FieldContext ctx)
    {
        var field = ctx.Field;
        var acceptAttr = field.AcceptedFileTypes is { Count: > 0 }
            ? $" accept=\"{string.Join(",", field.AcceptedFileTypes)}\""
            : string.Empty;
        var acceptList = field.AcceptedFileTypes is { Count: > 0 } ? string.Join(",", field.AcceptedFileTypes) : string.Empty;
        var maxSizeBytes = field.MaxSizeBytes ?? DefaultMaxFileSizeBytes;
        var alreadyUploaded = !string.IsNullOrEmpty(ctx.DisplayValue);
        var downloadUrl = alreadyUploaded && !string.IsNullOrEmpty(ctx.InstanceId)
            ? $"{ctx.FileEndpointBasePath}/files/{Uri.EscapeDataString(ctx.InstanceId)}/{Uri.EscapeDataString(field.FieldKey)}"
            : null;
        var uploadUrl = $"{ctx.FileEndpointBasePath}/upload/{Uri.EscapeDataString(ctx.InstanceId)}/{Uri.EscapeDataString(field.FieldKey)}";

        var uploadedBlock = $"""
            <div data-wayfinder-file-upload-uploaded{(alreadyUploaded ? "" : " hidden")}>
              <p class="govuk-body" data-wayfinder-file-upload-uploaded-text>
                Uploaded: <span data-wayfinder-file-upload-filename>{System.Net.WebUtility.HtmlEncode(ctx.DisplayValue)}</span>
                — <a class="govuk-link" data-wayfinder-file-upload-view-link href="{downloadUrl ?? "#"}" target="_blank" rel="noopener"{(downloadUrl is null ? " hidden" : "")}>View</a>
              </p>
              <button type="button" class="govuk-button govuk-button--secondary govuk-!-margin-bottom-2" data-module="govuk-button" data-wayfinder-file-upload-change>
                Choose a different file
              </button>
            </div>
            """;

        return $"""
            <div class="{ctx.WrapperClass}"{ctx.WrapperAttrs}
                 data-wayfinder-file-upload
                 data-wayfinder-upload-url="{uploadUrl}"
                 data-wayfinder-nonce="{ctx.Nonce}"
                 data-wayfinder-field-key="{field.FieldKey}"
                 data-wayfinder-max-size="{maxSizeBytes}"
                 data-wayfinder-accept="{acceptList}"
                 data-wayfinder-label="{field.Label}">
              <label class="govuk-label" for="{field.FieldKey}">{field.Label}{(field.Required ? """<span class="govuk-visually-hidden"> (required)</span>""" : "")}</label>
              {(ctx.HasHint ? $"""<div class="govuk-hint" id="{ctx.HintId}">{field.Hint}</div>""" : "")}
              {(ctx.HasFieldError ? $"""<p class="govuk-error-message" id="{ctx.ErrorId}"><span class="govuk-visually-hidden">Error:</span> {ctx.FieldError}</p>""" : "")}
              {uploadedBlock}
              <div class="wayfinder-file-upload-progress" data-wayfinder-file-upload-progress hidden>
                <p class="govuk-body" data-wayfinder-file-upload-progress-label>Uploading {field.Label}…</p>
                <div class="wayfinder-file-upload-progress-track" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"
                     aria-label="Upload progress for {field.Label}" data-wayfinder-file-upload-progress-bar>
                  <div class="wayfinder-file-upload-progress-fill" data-wayfinder-file-upload-progress-fill></div>
                </div>
                <span class="govuk-visually-hidden" aria-live="polite" data-wayfinder-file-upload-progress-announce></span>
              </div>
              <p class="govuk-error-message" data-wayfinder-file-upload-error hidden></p>
              <input class="govuk-file-upload{(ctx.HasFieldError ? " govuk-file-upload--error" : "")}"
                     type="file" id="{field.FieldKey}" name="{GovUk.FieldName(field.FieldKey)}"
                     data-wayfinder-file-upload-input data-label="{field.Label}"{acceptAttr}{ctx.DescribedBy}{ctx.AriaRequired}{ctx.AriaInvalid}
                     {(alreadyUploaded ? "hidden disabled" : "")} />
              <input type="hidden" name="{GovUk.FieldName(field.FieldKey)}" data-wayfinder-file-upload-token disabled value="" />
            </div>
            """;
    }
}
