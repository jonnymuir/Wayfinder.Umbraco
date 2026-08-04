using System.Text.Json;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Registers this package's own richer markup for the handful of types where
/// <c>Wayfinder.Rendering.GovUk</c>'s built-in default is a deliberate simplification (no real
/// GOV.UK Design System component exists for a slider/stat-card/chart, so the shared package
/// ships a plain, functionally-complete fallback rather than a lookalike of custom styling it
/// doesn't own). This package already had the richer bespoke markup (its own
/// <c>wayfinder-slider</c>/<c>wayfinder-stat-*</c>/<c>wayfinder-chart</c> CSS), so it registers
/// an override instead of downgrading to the shared default — exactly what the registry exists
/// for. <c>file-upload</c> is NOT here: its async progressive-upload markup needs per-request
/// context (instance id, nonce, upload URL) the registry's <c>(payload, errors)</c> delegate
/// signature has no room for, so it stays a permanent special case inside
/// <see cref="TagHelpers.ComponentTagHelper"/> instead.
/// </summary>
public static class WayfinderUmbracoRenderingOverrides
{
    public static void Register(GovUkComponentRenderer renderer)
    {
        renderer.RegisterField("slider", RenderSlider);
        renderer.RegisterComponent("stat-group", (component, _) => RenderStatGroup(component));
        renderer.RegisterComponent("chart", (component, _) => RenderChart(component));
    }

    private static string RenderSlider(FieldRenderPayload field, IReadOnlyDictionary<string, string> errors)
    {
        var min = field.Min ?? 0;
        var max = field.Max ?? 100;
        var value = string.IsNullOrEmpty(field.Value?.ToString()) ? min.ToString() : field.Value!.ToString()!;
        var prefix = field.Prefix ?? string.Empty;
        var suffix = field.Suffix ?? string.Empty;
        var hasError = errors.TryGetValue(field.FieldKey, out var error);
        // id stays the bare field key (plain CSS-selector-friendly, matching every other
        // rendered field); name carries GovUk.FieldName's "field:{fieldKey}" convention a
        // host's own form-submission parsing keys off. The two used to be the same string here,
        // which broke id-based selectors (see Wayfinder.Rendering.GovUk's own GovUkFields.Common
        // for the same fix and its full rationale).
        var id = field.FieldKey;
        var name = GovUk.FieldName(field.FieldKey);

        var hint = string.IsNullOrEmpty(field.Hint) ? "" : $"""<div id="{id}-hint" class="govuk-hint">{Esc(field.Hint)}</div>""";
        var errorMessage = error is null ? "" : $"""<p class="govuk-error-message" id="{id}-error"><span class="govuk-visually-hidden">Error:</span> {Esc(error)}</p>""";
        var describedByIds = string.Join(' ', new[] { string.IsNullOrEmpty(field.Hint) ? null : $"{id}-hint", hasError ? $"{id}-error" : null }.Where(v => v is not null));
        var describedBy = describedByIds.Length == 0 ? "" : $" aria-describedby=\"{describedByIds}\"";
        var required = field.Required ? "required" : "";

        return $"""
            <div class="govuk-form-group{(hasError ? " govuk-form-group--error" : "")}" data-wayfinder-slider>
              <label class="govuk-label" for="{id}">{Esc(field.Label)}</label>
              {hint}
              {errorMessage}
              <div class="wayfinder-slider__row">
                <input class="wayfinder-slider__input{(hasError ? " wayfinder-slider__input--error" : "")}"
                       type="range" id="{id}" name="{name}" value="{Esc(value)}"
                       data-label="{Esc(field.Label)}" data-wayfinder-slider-input{describedBy} {required}
                       min="{min}" max="{max}" step="{field.Step ?? 1}" />
                <span class="wayfinder-slider__value" data-wayfinder-slider-value
                      data-prefix="{Esc(prefix)}" data-suffix="{Esc(suffix)}" aria-hidden="true">{Esc(prefix)}{Esc(value)}{Esc(suffix)}</span>
              </div>
              <div class="wayfinder-slider__bounds" aria-hidden="true">
                <span>{Esc(prefix)}{min}{Esc(suffix)}</span>
                <span>{Esc(prefix)}{max}{Esc(suffix)}</span>
              </div>
            </div>
            """;
    }

    private static string RenderStatGroup(ComponentRenderPayload component)
    {
        var stats = component.Stats ?? Array.Empty<StatItem>();
        var heading = string.IsNullOrEmpty(component.Title) ? "" : $"""<h2 class="govuk-heading-m">{Esc(component.Title)}</h2>""";
        var cards = stats.Select(stat =>
        {
            var qualifier = string.IsNullOrEmpty(stat.Qualifier) ? "" : $"""<div class="wayfinder-stat-card__qualifier">{Esc(stat.Qualifier)}</div>""";
            return $"""
                <div class="wayfinder-stat-card{(stat.Emphasis ? " wayfinder-stat-card--emphasis" : "")}" data-wayfinder-stat="{Esc(stat.Label)}" data-wayfinder-stat-field="{Esc(stat.FieldKey)}">
                  <div class="wayfinder-stat-card__label">{Esc(stat.Label)}</div>
                  <div class="wayfinder-stat-card__value">{(string.IsNullOrEmpty(stat.Value) ? "—" : Esc(stat.Value))}</div>
                  {qualifier}
                </div>
                """;
        });

        return $"""
            {heading}
            <div class="wayfinder-stat-group" data-wayfinder-stat-group role="group" aria-label="{Esc(component.Title ?? "Key figures")}" aria-live="polite">
              {string.Join("\n", cards)}
            </div>
            """;
    }

    private static string RenderChart(ComponentRenderPayload component)
    {
        var chartJson = component.ChartJson ?? "null";
        using var doc = JsonDocument.Parse(chartJson);
        var chart = doc.RootElement;

        var palette = new[] { "#4f46e5", "#0d9488", "#b45309", "#6d28d9" };
        var bands = chart.TryGetProperty("bands", out var bandsElement)
            ? bandsElement.EnumerateArray().Select((band, index) => new
            {
                Key = band.GetProperty("key").GetString() ?? "",
                Label = band.GetProperty("label").GetString() ?? "",
                Color = band.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String
                    ? color.GetString()!
                    : palette[index % palette.Length]
            }).ToArray()
            : [];

        var xKey = chart.TryGetProperty("x", out var xElement) ? xElement.GetString() ?? "" : "";
        var xLabelEvery = chart.TryGetProperty("xLabelEvery", out var everyElement) ? everyElement.GetInt32() : 5;

        var rows = chart.TryGetProperty("rows", out var rowsElement)
            ? rowsElement.EnumerateArray().Select(row => new
            {
                X = row.GetProperty(xKey).GetDecimal(),
                Values = bands.Select(band => row.GetProperty(band.Key).GetDecimal()).ToArray()
            }).ToArray()
            : [];

        var maxTotal = rows.Length == 0 ? 1m : Math.Max(1m, rows.Max(r => r.Values.Sum()));
        const int plotHeight = 160;
        var safeConfig = chartJson.Replace("</", "<\\/");

        var legend = bands.Select(band => $"""
            <span class="wayfinder-chart__legend-item"><span class="wayfinder-chart__swatch" style="background:{band.Color}"></span>{Esc(band.Label)}</span>
            """);

        var bars = rows.Select(row =>
        {
            var segments = string.Join("", Enumerable.Range(0, bands.Length).Select(i =>
                $"""<div style="height:{Math.Round(row.Values[i] / maxTotal * plotHeight, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}px;background:{bands[i].Color}"></div>"""));
            return $"""<div class="wayfinder-chart__bar" title="{Esc(xKey)} {row.X}: {row.Values.Sum():N0}">{segments}</div>""";
        });

        var labels = rows.Select(row => $"<span>{(row.X % xLabelEvery == 0 ? row.X.ToString("0") : "")}</span>");

        var headerCells = string.Concat(bands.Select(band => $"""<th scope="col">{Esc(band.Label)}</th>"""));
        var tableRows = rows.Where((r, i) => r.X % xLabelEvery == 0 || i == 0).Select(row =>
        {
            var cells = string.Concat(row.Values.Select(v => $"<td>{v:N0}</td>"));
            return $"""<tr><th scope="row">{row.X:0}</th>{cells}</tr>""";
        });

        return $"""
            <figure class="wayfinder-chart" data-wayfinder-chart>
              <script type="application/json" data-wayfinder-chart-config>{safeConfig}</script>
              {(string.IsNullOrEmpty(component.Heading) ? "" : $"""<figcaption class="wayfinder-chart__title">{Esc(component.Heading)}</figcaption>""")}
              <div class="wayfinder-chart__legend">{string.Join("\n", legend)}</div>
              <div class="wayfinder-chart__plot" data-wayfinder-chart-plot aria-hidden="true">{string.Join("\n", bars)}</div>
              <div class="wayfinder-chart__labels" data-wayfinder-chart-labels aria-hidden="true">{string.Join("\n", labels)}</div>
              <table class="wayfinder-visually-hidden" data-wayfinder-chart-table>
                <caption>{Esc(component.Heading ?? "Chart data")}</caption>
                <thead><tr><th scope="col">{Esc(xKey)}</th>{headerCells}</tr></thead>
                <tbody>{string.Join("\n", tableRows)}</tbody>
              </table>
            </figure>
            """;
    }

    private static string Esc(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
}
