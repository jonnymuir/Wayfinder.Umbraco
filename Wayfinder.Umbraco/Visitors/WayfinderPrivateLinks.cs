using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Visitors;

/// <summary>
/// Following a private link from an email, the way back to a visitor's request on any device. The link
/// never changes who a browser is by itself: it takes the person to the request's page, which asks them
/// to continue, and only that answer (a same-site form post) adds this browser to the visitor. So a link
/// someone else sends you, to their request, can't quietly make your browser theirs. A request that now
/// belongs to someone signed in opens only for them, and a team member is sent to the team side.
/// </summary>
public sealed class WayfinderPrivateLinks(
    IPrivateLinkStore links,
    IServiceRequestStore requests,
    IOptions<WayfinderServiceDesignOptions> serviceOptions,
    IOptions<WayfinderVisitorOptions> visitorOptions)
{
    /// <summary>Where an email's link points, under the site's public address.</summary>
    public const string LinkPath = "/wayfinder/link";

    /// <summary>The short-lived cookie holding a followed link's token until the person answers.</summary>
    public const string PendingCookieName = "Wayfinder.PrivateLink";

    internal static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    /// <summary>A new link to <paramref name="instanceId"/> on <paramref name="pagePath"/>, as an absolute URL under <paramref name="publicBaseUrl"/>.</summary>
    public string Issue(string publicBaseUrl, string instanceId, string pagePath) =>
        $"{publicBaseUrl.TrimEnd('/')}{LinkPath}?t={Uri.EscapeDataString(links.Issue(instanceId, pagePath))}";

    /// <summary>
    /// What a token opens for the current request, or null when the token is unknown or expired, or its
    /// request has gone or belongs to another tenant.
    /// </summary>
    public async Task<PrivateLinkTarget?> ResolveAsync(HttpContext context, string? token)
    {
        if (links.Redeem(token) is not { } link
            || !requests.TryGet(link.InstanceId, out var request)
            || !string.Equals(request.TenantId, serviceOptions.Value.ResolveTenantId!(context), StringComparison.Ordinal))
        {
            return null;
        }

        var profile = await serviceOptions.Value.ResolveAccessProfile!(context, request.BlueprintKey);
        return new PrivateLinkTarget(link, request, profile, visitorOptions.Value.SignedInUserId(context));
    }

    /// <summary>The question to ask on <paramref name="blueprintKey"/>'s page, if this browser followed a link to one of its requests.</summary>
    public async Task<PrivateLinkPrompt?> PendingAsync(HttpContext context, string blueprintKey, string blueprintDisplayName)
    {
        if (!context.Request.Cookies.TryGetValue(PendingCookieName, out var token)
            || await ResolveAsync(context, token) is not { } target
            || !string.Equals(target.Request.BlueprintKey, blueprintKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new PrivateLinkPrompt(LinkPath, blueprintDisplayName);
    }
}

/// <param name="Link">The link.</param>
/// <param name="Request">The request it opens.</param>
/// <param name="AccessProfile">The current person's access profile for that request's blueprint.</param>
/// <param name="SignedInUserId">Who is signed in, or null.</param>
public sealed record PrivateLinkTarget(PrivateLink Link, ServiceRequest Request, ActorProfile AccessProfile, string? SignedInUserId)
{
    /// <summary>A team member: someone whose access is not limited to their own requests.</summary>
    public bool IsStaff => SignedInUserId is not null && !AccessProfile.RestrictToInstanceOwner;

    /// <summary>The request's page, opened on the request.</summary>
    public string RequestPage =>
        $"{Link.PagePath}{(Link.PagePath.Contains('?') ? '&' : '?')}instanceId={Uri.EscapeDataString(Request.InstanceId)}";
}

/// <summary>For <c>~/Views/Partials/_WayfinderPrivateLink.cshtml</c>: the question a followed link asks.</summary>
/// <param name="PostUrl">Where the answer is posted (field <c>decision</c>: <c>continue</c> or <c>dismiss</c>).</param>
/// <param name="BlueprintDisplayName">The service the request belongs to, to name it.</param>
public sealed record PrivateLinkPrompt(string PostUrl, string BlueprintDisplayName);
