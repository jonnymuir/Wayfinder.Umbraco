using System.Globalization;
using Microsoft.Extensions.Options;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Times as people read them, in <see cref="WayfinderServiceDesignOptions.DisplayTimeZone"/>. Stage
/// rendering uses it for conversation messages; a host's own views (an overridden worklist, an email
/// template) can inject it for theirs.
/// </summary>
public sealed class WayfinderDisplayTime(IOptions<WayfinderServiceDesignOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.DisplayTimeZone);

    /// <summary>The same instant, carrying the display time zone's offset at that moment.</summary>
    public DateTimeOffset Convert(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, _zone);

    public string Format(DateTimeOffset at, string format = "d MMMM yyyy, HH:mm") =>
        Convert(at).ToString(format, CultureInfo.InvariantCulture);

    /// <summary>The envelope with every conversation message's time in the display time zone.</summary>
    public ServiceRequestResponseEnvelope InDisplayTime(ServiceRequestResponseEnvelope envelope) =>
        envelope.Render is not { } render
            ? envelope
            : envelope with
            {
                Render = render with
                {
                    Components = render.Components.Select(component => component with
                    {
                        Fields = Fields(component.Fields),
                        AccordionSections = component.AccordionSections?
                            .Select(section => section with { Fields = Fields(section.Fields) })
                            .ToArray(),
                    }).ToArray(),
                },
            };

    private FieldRenderPayload[] Fields(IEnumerable<FieldRenderPayload> fields) =>
        fields.Select(field => field.Conversation is { } messages
                ? field with { Conversation = messages.Select(m => m with { At = Convert(m.At) }).ToArray() }
                : field)
            .ToArray();
}
