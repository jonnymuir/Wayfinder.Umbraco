using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using Moq;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;
using Wayfinder.Umbraco.TagHelpers;

namespace Wayfinder.Umbraco.Tests.TagHelpers;

/// <summary>
/// SECURITY REGRESSION: <see cref="ComponentTagHelper"/>'s <c>file-upload</c> renderer used to
/// interpolate blueprint-authored <c>Label</c>/<c>Hint</c>/field-error text straight into markup
/// with no encoding, unlike every equivalent renderer in
/// <c>Wayfinder.Rendering.GovUk.GovUkFields</c> (which uses <see cref="GovUk.Esc"/> throughout).
/// A label containing a quote broke out of an attribute; a label containing a tag injected into
/// the page. These tests assert the rendered field-upload markup encodes every one of those
/// values, exercised through the tag helper's real <c>ProcessAsync</c> seam.
/// </summary>
public sealed class ComponentTagHelperFileUploadTests
{
    private const string MaliciousLabel = """"Evidence"><script>alert(1)</script>"""";

    private static async Task<string> RenderFileUploadFieldAsync(FieldRenderPayload field)
    {
        var viewEngine = new Mock<ICompositeViewEngine>();
        viewEngine
            .Setup(v => v.GetView(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(ViewEngineResult.NotFound("irrelevant", []));

        var options = Options.Create(new WayfinderServiceDesignOptions());
        var tagHelper = new ComponentTagHelper(
            Mock.Of<IHtmlHelper>(),
            new ComponentPartialResolver(viewEngine.Object),
            new GovUkComponentRenderer(),
            options)
        {
            Field = field,
        };

        var context = new TagHelperContext(
            new TagHelperAttributeList(), new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));
        var output = new TagHelperOutput(
            "wayfinder-field", new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        await tagHelper.ProcessAsync(context, output);

        return output.Content.GetContent();
    }

    [Fact]
    public async Task FileUpload_EncodesAMaliciousLabel_InEveryRenderedLocation()
    {
        var field = new FieldRenderPayload
        {
            FieldKey = "evidence",
            Label = MaliciousLabel,
            FieldType = "file-upload",
            Required = false,
        };

        var html = await RenderFileUploadFieldAsync(field);

        html.Should().NotContain("<script>alert(1)</script>",
            "a blueprint-authored label must never inject a live script tag into a citizen-facing page");
        html.Should().NotContain(""""Evidence">"""",
            "an unescaped quote in the label must never let the value break out of its HTML attribute");
    }

    [Fact]
    public async Task FileUpload_EncodesAMaliciousHint()
    {
        var field = new FieldRenderPayload
        {
            FieldKey = "evidence",
            Label = "Evidence",
            Hint = """<img src=x onerror=alert(1)>""",
            FieldType = "file-upload",
            Required = false,
        };

        var html = await RenderFileUploadFieldAsync(field);

        html.Should().NotContain("<img src=x onerror=alert(1)>",
            "a blueprint-authored hint must never inject a live event handler into a citizen-facing page");
    }

    [Fact]
    public async Task FileUpload_EncodesTheAcceptedFileTypesList()
    {
        var field = new FieldRenderPayload
        {
            FieldKey = "evidence",
            Label = "Evidence",
            FieldType = "file-upload",
            Required = false,
            AcceptedFileTypes = ["""".pdf"><script>alert(1)</script>""""],
        };

        var html = await RenderFileUploadFieldAsync(field);

        html.Should().NotContain("<script>alert(1)</script>",
            "a blueprint-authored accepted-file-types entry must never inject a live script tag");
    }
}
