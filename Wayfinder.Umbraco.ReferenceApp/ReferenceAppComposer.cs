using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// Registers the demo cookie authentication scheme as the app-wide default — an <see cref="IComposer"/>,
/// not a plain call in Program.cs, because Umbraco's own <c>AddBackOffice()</c> registers its own
/// default authentication scheme too, and whichever registration runs LAST wins (ASP.NET Core's
/// <c>AddAuthentication(...)</c> is a last-write-wins <c>Configure&lt;AuthenticationOptions&gt;</c>
/// call, not additive). Composers run during Umbraco's own boot sequence, after
/// <c>AddBackOffice()</c>'s eager registration — the same reason
/// <c>UmbracoPrism.Core.PrismComposer</c> registers its own member authentication scheme this way
/// rather than directly in Program.cs.
///
/// Must set <see cref="AuthenticationOptions.DefaultAuthenticateScheme"/>/
/// <see cref="AuthenticationOptions.DefaultChallengeScheme"/>/<see cref="AuthenticationOptions.DefaultSignInScheme"/>
/// individually — NOT just the single-string <c>AddAuthentication(defaultScheme)</c> overload,
/// which only sets <see cref="AuthenticationOptions.DefaultScheme"/>. Confirmed live via a debug
/// dump of the resolved <see cref="AuthenticationOptions"/> at boot: Umbraco's own backoffice
/// registration explicitly sets <c>DefaultAuthenticateScheme = "Identity.Application"</c>, and
/// ASP.NET Core's <c>AuthenticateAsync()</c> (called by <c>UseAuthentication()</c> with no scheme
/// argument) prefers that explicit value over the plain <c>DefaultScheme</c> fallback — so setting
/// only <c>DefaultScheme</c> left every front-end request's <c>HttpContext.User</c> permanently
/// unauthenticated under Umbraco's own Identity scheme, even immediately after a successful demo
/// sign-in. This is exactly why <c>PrismComposer</c> sets all three individually too.
/// </summary>
public class ReferenceAppComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        var authBuilder = builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = ReferenceAppAuth.SchemeName;
            options.DefaultSignInScheme = ReferenceAppAuth.SchemeName;
            options.DefaultChallengeScheme = ReferenceAppAuth.SchemeName;
        });

        authBuilder.AddCookie(ReferenceAppAuth.SchemeName, options =>
        {
            options.LoginPath = "/demo/login";
            options.Cookie.Name = "WayfinderUmbracoReferenceApp";
        });

        // Wayfinder.Umbraco's ServiceRequestPollController requires this named policy — the
        // package deliberately registers no opinion of its own on how a host authenticates (see
        // WayfinderUmbracoAuthorizationPolicies.ServiceRequestPolling's own remarks: "a host must
        // register this policy"). Missing it isn't a visible failure at first glance: the waiting
        // screen's own poll script (_Stage-Waiting.cshtml) just gets a denied request, catches it,
        // and silently retries forever — the page never live-updates, but a manual refresh always
        // works (a fresh page load re-evaluates state directly, no poll endpoint involved), which
        // is exactly what made this easy to miss until someone actually sat and watched the wait
        // screen. Any authenticated demo persona may poll — GetCurrent itself already scopes the
        // result to that caller's own userId/ActorProfile, so this policy only needs to gate
        // "signed in at all", not re-implement ownership checking.
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(WayfinderUmbracoAuthorizationPolicies.ServiceRequestPolling,
                policy => policy.RequireAuthenticatedUser());
        });
    }
}
