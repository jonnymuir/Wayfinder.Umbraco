namespace Wayfinder.Umbraco.Emails;

/// <summary>Settings for <see cref="Extensions.WayfinderEmailBuilderExtensions.AddWayfinderEmails"/>.</summary>
public sealed class WayfinderEmailOptions
{
    /// <summary>The sender every email goes from, such as <c>"The Exchange &lt;noreply@example.com&gt;"</c>.</summary>
    public string From { get; set; } = "";

    /// <summary>
    /// The site's public address (<c>https://example.com</c>), for links in emails. From configuration,
    /// never from a request, so a request's Host header can't decide where an email's link points.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>The emails, each sent when its blueprint's requests make one of its moves.</summary>
    public List<WayfinderEmail> Emails { get; } = [];
}

/// <summary>An email a service sends when one of its requests makes a particular move.</summary>
public sealed class WayfinderEmail
{
    /// <summary>A name for this email, unique among them, for logs and queued jobs.</summary>
    public required string Name { get; init; }

    public required string BlueprintKey { get; init; }

    /// <summary>The route triggers (moves) that send it, such as <c>["reply", "close"]</c>.</summary>
    public required IReadOnlyList<string> Actions { get; init; }

    public required string Subject { get; init; }

    /// <summary>
    /// The Razor view that renders the HTML body, such as <c>"~/Views/Emails/ContactReply.cshtml"</c>,
    /// with a <see cref="WayfinderEmailModel"/> model. It renders outside any page, so start it with
    /// <c>@inherits Microsoft.AspNetCore.Mvc.Razor.RazorPage&lt;Wayfinder.Umbraco.Emails.WayfinderEmailModel&gt;</c>
    /// rather than an Umbraco page base.
    /// </summary>
    public required string Template { get; init; }

    /// <summary>The request field holding the address to send to (the person who wrote in), if any.</summary>
    public string? ToField { get; init; }

    /// <summary>Fixed addresses to send to (a team), if any.</summary>
    public IReadOnlyList<string> ToAddresses { get; init; } = [];

    /// <summary>
    /// When set, the email carries a private link (<see cref="WayfinderEmailModel.PrivateLinkUrl"/>) that
    /// opens the request on this page, such as <c>"/contact-us/"</c>. Needs <c>AddWayfinderVisitors</c>.
    /// </summary>
    public string? PrivateLinkPagePath { get; init; }
}
