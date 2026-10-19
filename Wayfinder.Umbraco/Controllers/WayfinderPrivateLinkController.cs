using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// Where a private link from an email lands (GET), and where the person's answer to "continue in this
/// browser?" goes (POST). See <see cref="WayfinderPrivateLinks"/> for why following a link asks first.
/// Registered by <see cref="Extensions.WayfinderVisitorServiceCollectionExtensions.AddWayfinderVisitors"/>.
/// </summary>
public sealed class WayfinderPrivateLinkController(
    IServiceProvider services,
    IProcessManager processManager,
    IOptions<WayfinderServiceDesignOptions> serviceOptions,
    IOptions<WayfinderVisitorOptions> visitorOptions) : Controller
{
    // Resolved per action rather than injected: a site that hasn't called AddWayfinderVisitors has no
    // private links, and these routes answer 404 there instead of failing to build the controller.
    private WayfinderPrivateLinks? PrivateLinks => services.GetService<WayfinderPrivateLinks>();

    private WayfinderVisitors Visitors => services.GetRequiredService<WayfinderVisitors>();

    // Anonymous on purpose: the person following a link is usually not signed in, and the token in
    // it is what proves the request is theirs. This GET changes no identity; it only remembers the
    // link for the request's page to ask about.
    [AllowAnonymous]
    [HttpGet(WayfinderPrivateLinks.LinkPath)]
    public async Task<IActionResult> Open(string? t)
    {
        KeepTheTokenHere();
        if (PrivateLinks is not { } privateLinks)
        {
            return NotFound();
        }

        if (await privateLinks.ResolveAsync(HttpContext, t) is not { } target)
        {
            return Redirect(LocalOrRoot(visitorOptions.Value.ExpiredLinkPath));
        }

        if (target.IsStaff)
        {
            return Redirect(LocalOrRoot(visitorOptions.Value.StaffLandingPath));
        }

        // Someone signed in owns it now: only they can open it, and the token adds nothing, so it goes
        // no further (not into a sign-in return address, not into the page's address).
        if (!WayfinderVisitors.IsVisitorUserId(target.Request.UserId))
        {
            return target.SignedInUserId == target.Request.UserId
                ? Redirect(LocalOrRoot(target.RequestPage))
                : Redirect($"{serviceOptions.Value.LoginPath}?ReturnUrl={Uri.EscapeDataString(target.RequestPage)}");
        }

        if (target.SignedInUserId is null && Visitors.PeekVisitorUserId(HttpContext) == target.Request.UserId)
        {
            return Redirect(LocalOrRoot(target.RequestPage));
        }

        WayfinderVisitors.WriteCookie(HttpContext, WayfinderPrivateLinks.PendingCookieName, t!, WayfinderPrivateLinks.PendingLifetime);
        return Redirect(LocalOrRoot(target.Link.PagePath));
    }

    // Anonymous for the same reason as Open; the antiforgery token makes it an answer given on this
    // site's own page, never a post another site makes the browser send.
    [AllowAnonymous]
    [HttpPost(WayfinderPrivateLinks.LinkPath)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Answer(string? decision)
    {
        KeepTheTokenHere();
        if (PrivateLinks is not { } privateLinks)
        {
            return NotFound();
        }

        Request.Cookies.TryGetValue(WayfinderPrivateLinks.PendingCookieName, out var token);
        Response.Cookies.Delete(WayfinderPrivateLinks.PendingCookieName, WayfinderVisitors.CookieOptions(null));

        if (await privateLinks.ResolveAsync(HttpContext, token) is not { } target
            || !WayfinderVisitors.IsVisitorUserId(target.Request.UserId))
        {
            return Redirect(LocalOrRoot(visitorOptions.Value.ExpiredLinkPath));
        }

        if (target.IsStaff)
        {
            return Redirect(LocalOrRoot(visitorOptions.Value.StaffLandingPath));
        }

        if (decision != "continue")
        {
            return Redirect(LocalOrRoot(target.Link.PagePath));
        }

        if (target.SignedInUserId is { } signedIn)
        {
            processManager.ClaimInstances(target.Request.TenantId, target.Request.UserId, signedIn);
        }
        else
        {
            Visitors.AddThisBrowser(HttpContext, target.Request.UserId);
        }

        return Redirect(LocalOrRoot(target.RequestPage));
    }

    // The token must not reach anything the next page loads, be cached, or be framed by another site.
    private void KeepTheTokenHere()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Frame-Options"] = "DENY";
    }

    private static string LocalOrRoot(string path) => WayfinderVisitors.IsLocalPath(path) ? path : "/";
}
