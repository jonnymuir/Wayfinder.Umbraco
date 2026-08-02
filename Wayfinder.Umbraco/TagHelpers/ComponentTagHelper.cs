using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Net;
using Wayfinder.Umbraco.Models;
using Wayfinder.Umbraco.Services;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.TagHelpers;

/// <summary>
/// Renders a service blueprint component (container) or field (input) by dispatching to a
/// convention-based Razor partial — see <see cref="ComponentPartialResolver"/> for exactly how
/// (and where) a host can override any type, and why the package's own catalog lives at a
/// different path than the one a host uses.
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
/// own _Component-Default.cshtml can also be overridden as the catch-all fallback.
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

    public ComponentTagHelper(IHtmlHelper htmlHelper, ComponentPartialResolver partialResolver)
    {
        _htmlHelper = htmlHelper;
        _partialResolver = partialResolver;
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

        ((IViewContextAware)_htmlHelper).Contextualize(ViewContext);

        var ctx = new ComponentContext
        {
            Component    = Component,
            Errors       = Errors       ?? new Dictionary<string, string>(),
            Values       = Values       ?? new Dictionary<string, string>(),
            ReturnUrl    = ReturnUrl,
            InstanceId   = InstanceId,
            StateVersion = StateVersion,
            BlueprintKey  = BlueprintKey,
            Nonce        = Nonce
        };

        var partial = _partialResolver.ResolveComponentPartial(Component.Type);
        var content = await _htmlHelper.PartialAsync(partial, ctx);

        output.Content.SetHtmlContent(content);

        // Live visibility: emit the showWhen expression for the client runtime and the
        // server-evaluated hidden state, wrapping the rendered component.
        if (!string.IsNullOrEmpty(Component.ShowWhen))
        {
            var expression = WebUtility.HtmlEncode(Component.ShowWhen);
            var hidden = Component.Hidden ? " hidden" : string.Empty;
            output.PreContent.SetHtmlContent($@"<div data-wayfinder-show-when=""{expression}""{hidden}>");
            output.PostContent.SetHtmlContent("</div>");
        }
    }

    private async Task ProcessFieldAsync(TagHelperOutput output)
    {
        ((IViewContextAware)_htmlHelper).Contextualize(ViewContext);

        var fieldType = (Field!.FieldType ?? "text").ToLowerInvariant();

        // Content-only field types rendered inline — they are not form controls
        // and do not need the govuk-form-group wrapper or the partial dispatch system.
        var inlineHtml = RenderInlineFieldType(fieldType);
        if (inlineHtml is not null)
        {
            output.Content.SetHtmlContent(inlineHtml);
            return;
        }

        var fieldError = Errors?.GetValueOrDefault(Field.FieldKey);
        var ctx        = FieldContext.Build(Field, fieldError, Values, InstanceId, Nonce, BlueprintKey);
        var partial    = _partialResolver.ResolveFieldPartial(fieldType);
        var content    = await _htmlHelper.PartialAsync(partial, ctx);

        output.Content.SetHtmlContent(content);
    }

    /// <summary>
    /// Renders content-only field types that are not form controls.
    /// Returns null for standard field types that use the partial system.
    /// </summary>
    private string? RenderInlineFieldType(string fieldType)
    {
        var content = Field!.Content;
        var encodedContent = string.IsNullOrEmpty(content) ? string.Empty : WebUtility.HtmlEncode(content);
        var encodedLabel = string.IsNullOrEmpty(Field.Label) ? string.Empty : WebUtility.HtmlEncode(Field.Label);
        var bannerTitleId = string.IsNullOrEmpty(Field.FieldKey)
            ? "wayfinder-inline-banner-title"
            : $"wayfinder-inline-banner-title-{SanitizeIdFragment(Field.FieldKey)}";

        return fieldType switch
        {
            "inset-text" when !string.IsNullOrEmpty(content) =>
                $@"<div class=""govuk-inset-text"">{encodedContent}</div>",

            "warning-text" when !string.IsNullOrEmpty(content) =>
                $@"<div class=""govuk-warning-text"">
  <span class=""govuk-warning-text__icon"" aria-hidden=""true"">!</span>
  <strong class=""govuk-warning-text__text"">
    <span class=""govuk-visually-hidden"">Warning</span>
    {encodedContent}
  </strong>
</div>",

            "details" when !string.IsNullOrEmpty(content) =>
                $@"<details class=""govuk-details"">
  <summary class=""govuk-details__summary"">
    <span class=""govuk-details__summary-text"">{(string.IsNullOrEmpty(encodedLabel) ? "More information" : encodedLabel)}</span>
  </summary>
  <div class=""govuk-details__text"">{encodedContent}</div>
</details>",

            "notification-banner" when !string.IsNullOrEmpty(content) =>
                $@"<div class=""govuk-notification-banner"" role=""region"" aria-labelledby=""{bannerTitleId}"">
  <div class=""govuk-notification-banner__header"">
    <h2 class=""govuk-notification-banner__title"" id=""{bannerTitleId}"">{(string.IsNullOrEmpty(encodedLabel) ? "Information" : encodedLabel)}</h2>
  </div>
  <div class=""govuk-notification-banner__content"">
    <p class=""govuk-body"">{encodedContent}</p>
  </div>
</div>",

            "body" when !string.IsNullOrEmpty(content) =>
                $@"<p class=""govuk-body"">{encodedContent}</p>",

            "heading" when !string.IsNullOrEmpty(content) =>
                $@"<h2 class=""govuk-heading-m"">{encodedContent}</h2>",

            "inset-text" or "warning-text" or "details" or "notification-banner" or "body" or "heading"
                => string.Empty, // content was null/empty — suppress

            _ => null // use the partial dispatch system
        };
    }

    private static string SanitizeIdFragment(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
}
