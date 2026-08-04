using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// Proves the four types this package keeps its own richer markup for (rather than the shared
/// package's deliberately plain defaults) actually render via the registered override, not the
/// Wayfinder.Rendering.GovUk built-in — and that everything else still falls through correctly.
/// </summary>
public class WayfinderUmbracoRenderingOverridesTests
{
    private static GovUkComponentRenderer BuildRenderer()
    {
        var renderer = new GovUkComponentRenderer();
        WayfinderUmbracoRenderingOverrides.Register(renderer);
        return renderer;
    }

    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    [Fact]
    public void Slider_UsesThisPackagesBespokeMarkupNotTheSharedDefault()
    {
        var renderer = BuildRenderer();
        var html = renderer.RenderField(new FieldRenderPayload
        {
            FieldKey = "risk",
            Label = "Risk appetite",
            FieldType = "slider",
            Required = true,
            Min = 0,
            Max = 10,
        }, NoErrors);

        Assert.Contains("data-wayfinder-slider", html);
        Assert.Contains("wayfinder-slider__input", html);
        // The shared package's plain default has no wayfinder-slider__* classes at all.
        Assert.DoesNotContain("<output", html);
    }

    // Regression coverage: id and name used to share the same "field:{fieldKey}"-prefixed
    // string here too, breaking any plain CSS ID selector targeting the rendered input (a
    // colon in an id isn't safely selectable via #id without escaping) — see
    // Wayfinder.Rendering.GovUk's own GovUkFields.Common for the sibling fix and full rationale.
    [Fact]
    public void Slider_IdStaysBareFieldKey_NameCarriesFieldPrefix()
    {
        var renderer = BuildRenderer();
        var html = renderer.RenderField(new FieldRenderPayload
        {
            FieldKey = "risk",
            Label = "Risk appetite",
            FieldType = "slider",
            Required = true,
            Min = 0,
            Max = 10,
        }, NoErrors);

        Assert.Contains("id=\"risk\"", html);
        Assert.Contains("for=\"risk\"", html);
        Assert.Contains("name=\"field:risk\"", html);
        Assert.DoesNotContain("id=\"field:risk\"", html);
    }

    [Fact]
    public void StatGroup_UsesThisPackagesBespokeMarkupNotTheSharedDefault()
    {
        var renderer = BuildRenderer();
        var html = renderer.RenderComponent(new ComponentRenderPayload
        {
            Type = "stat-group",
            Title = "Key figures",
            Stats = [new() { Label = "Annual pension", FieldKey = "pension", Value = "16400" }],
        }, NoErrors);

        Assert.Contains("wayfinder-stat-group", html);
        Assert.Contains("wayfinder-stat-card", html);
        Assert.Contains("Annual pension", html);
    }

    [Fact]
    public void Chart_UsesThisPackagesBespokeMarkupNotTheSharedDefault()
    {
        var renderer = BuildRenderer();
        var chartJson = """
            {
              "x": "age",
              "bands": [{ "key": "pension", "label": "Pension" }],
              "rows": [{ "age": 66, "pension": 12000 }]
            }
            """;
        var html = renderer.RenderComponent(new ComponentRenderPayload
        {
            Type = "chart",
            Heading = "Projected income",
            ChartJson = chartJson,
        }, NoErrors);

        Assert.Contains("wayfinder-chart", html);
        Assert.Contains("data-wayfinder-chart-plot", html);
        Assert.Contains("Pension", html);
    }

    [Fact]
    public void UnregisteredTypes_StillFallThroughToTheSharedPackagesBuiltIn()
    {
        var renderer = BuildRenderer();
        var html = renderer.RenderField(new FieldRenderPayload
        {
            FieldKey = "name",
            Label = "Full name",
            FieldType = "text",
            Required = true,
        }, NoErrors);

        Assert.Contains("govuk-input", html);
        Assert.Contains("Full name", html);
    }
}
