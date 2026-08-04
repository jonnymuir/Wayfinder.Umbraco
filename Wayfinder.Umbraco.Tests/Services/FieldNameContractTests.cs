using System.Text.RegularExpressions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// GovUk.FieldName's "field:{fieldKey}" convention and ServiceRequestPageController's own
/// submitted-field parsing are maintained independently — nothing in the compiler ties them
/// together. They drifted apart once already: this package adopted Wayfinder.Rendering.GovUk
/// (whose fields post as "field:{fieldKey}") but the controller kept parsing the old
/// "fields[{fieldKey}]" bracket form its own now-removed Razor partials used to emit, so every
/// real HTML form submission silently posted zero recognized fields — everything came back
/// "required" regardless of what the visitor entered or checked. Locks the contract down instead
/// of relying on discovering the mismatch via a live end-to-end journey.
/// </summary>
public class FieldNameContractTests
{
    // Mirrors ServiceRequestPageController.Advance's own submitted-field extraction exactly —
    // if that logic ever changes, update it here too, in step.
    private const string FieldPrefix = "field:";

    private static string ExtractFieldKey(string formKey) =>
        formKey.StartsWith(FieldPrefix, StringComparison.Ordinal)
            ? formKey[FieldPrefix.Length..]
            : throw new InvalidOperationException($"'{formKey}' does not carry the controller's expected \"{FieldPrefix}\" prefix.");

    [Theory]
    [InlineData("age-confirmation")]
    [InlineData("full-name")]
    [InlineData("declaration")]
    public void RenderedFieldName_RoundTripsThroughTheControllersOwnParsing(string fieldKey)
    {
        var renderer = new GovUkComponentRenderer();
        var html = renderer.RenderField(new FieldRenderPayload
        {
            FieldKey = fieldKey,
            Label = "Some label",
            FieldType = "boolean",
            Required = true,
        }, new Dictionary<string, string>());

        var match = Regex.Match(html, "name=\"([^\"]+)\"");
        Assert.True(match.Success, $"Expected a name=\"...\" attribute in the rendered HTML:\n{html}");

        var renderedName = match.Groups[1].Value;
        Assert.Equal(GovUk.FieldName(fieldKey), renderedName);
        Assert.Equal(fieldKey, ExtractFieldKey(renderedName));
    }
}
