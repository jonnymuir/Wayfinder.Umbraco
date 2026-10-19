using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Wayfinder.Umbraco.Visitors;

/// <summary>Settings for <see cref="Extensions.WayfinderVisitorServiceCollectionExtensions.AddWayfinderVisitors"/>.</summary>
public sealed class WayfinderVisitorOptions
{
    /// <summary>
    /// The user id of whoever is signed in, or null for an anonymous visitor. Defaults to the Umbraco
    /// member signed in with the members' own scheme (<c>Identity.Application</c>), as
    /// <c>member:{id}</c>; only that scheme counts, so an identity from any other scheme on the request
    /// (an external provider's, say) is never taken for a member. A site with its own sign-in sets this.
    /// </summary>
    public Func<HttpContext, string?> SignedInUserId { get; set; } = UmbracoMemberUserId;

    /// <summary>The visitor cookie's name. Defaults to <c>Wayfinder.Visitor</c>.</summary>
    public string CookieName { get; set; } = "Wayfinder.Visitor";

    /// <summary>
    /// How long the visitor cookie lasts since the visitor was last here, so they can come back to a
    /// request from the same browser weeks later. Defaults to 180 days.
    /// </summary>
    public TimeSpan CookieLifetime { get; set; } = TimeSpan.FromDays(180);

    /// <summary>How long a private link in an email works. Defaults to 30 days.</summary>
    public TimeSpan PrivateLinkLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Where a team member (anyone whose access profile is not limited to their own requests) who opens a
    /// private link is sent: the team side, where they deal with requests. Opening one never changes who
    /// they are. Defaults to <c>/</c>.
    /// </summary>
    public string StaffLandingPath { get; set; } = "/";

    /// <summary>Where a private link that is unknown or has expired takes the person. Defaults to <c>/</c>.</summary>
    public string ExpiredLinkPath { get; set; } = "/";

    private static string? UmbracoMemberUserId(HttpContext context) =>
        context.User.Identities
            .FirstOrDefault(identity => identity.IsAuthenticated
                && string.Equals(identity.AuthenticationType, IdentityConstants.ApplicationScheme, StringComparison.Ordinal))
            ?.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } id
            ? $"member:{id}"
            : null;
}
