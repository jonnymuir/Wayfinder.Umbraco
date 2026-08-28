using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Engine.Mcp;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco;
using Wayfinder.Umbraco.Extensions;
using Wayfinder.Umbraco.ReferenceApp;

var builder = WebApplication.CreateBuilder(args);

// Local secrets override — gitignored.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// The demo cookie authentication scheme itself is registered as the app-wide default in
// ReferenceAppComposer, not here — see that class's own remarks for why it must be a composer.

builder.Services.AddWayfinderUmbraco(options =>
{
    options.ResolveTenantId = _ => "reference";
    options.ResolveUserId = ctx =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    options.ResolveAccessProfile = ReferenceAppAuth.ResolveAccessProfile;
});

// AddWayfinderUmbraco() above already registers ServiceBlueprintAuthoringService (the same
// transport-agnostic service the backoffice REST authoring controller uses) — this just adds
// the MCP transport over it. Anonymous by design in Wayfinder.Engine.Mcp itself; this app
// chains its own RequireAuthorization() onto the mapped endpoint below, same convention as
// Wayfinder.ReferenceApp documents for the REST authoring API.
builder.Services.AddServiceBlueprintAuthoringMcp();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

builder.Services.AddSingleton<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceContentSeeder>();
builder.Services.AddSingleton<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceBlueprintSeeder>();
// Scoped, not Singleton like the other two seeders — IBackOfficeUserClientCredentialsManager is
// itself registered Scoped by Umbraco, and DI validation fails fast on a Singleton consuming a
// Scoped dependency (confirmed live).
builder.Services.AddScoped<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceMcpDemoAgentSeeder>();

var app = builder.Build();

ReferenceAppAuth.MapDemoLoginRoutes(app);

await app.BootUmbracoAsync();

// UseAuthentication()/UseAuthorization() must run inside WithMiddleware, not before it — Umbraco's
// own UseUmbraco() sets up UseRouting() internally, and WithMiddleware is the documented extension
// point for anything that needs to run between routing and endpoint dispatch, which is exactly
// where these belong.
app.UseUmbraco()
    .WithMiddleware(u =>
    {
        // Wayfinder.Rendering.GovUk's own vendored govuk-frontend CSS/JS (served automatically
        // as a static web asset under _content/Wayfinder.Rendering.GovUk/... once UseStaticFiles()
        // runs) plus its own font re-rooting — govuk-frontend.min.css's @font-face rules request
        // fonts at a hard-coded absolute "/assets/fonts/...", regardless of where the CSS itself is
        // served from. See Wayfinder.ReferenceApp/Program.cs for the same pattern in the core repo.
        u.AppBuilder.UseStaticFiles();
        u.AppBuilder.UseGovUkFrontendAssets();

        u.AppBuilder.UseAuthentication();
        u.AppBuilder.UseAuthorization();
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

// Endpoint registration isn't order-dependent on the middleware pipeline structure above (only
// enforcement is, via UseAuthorization() already wired inside WithMiddleware) — mapped here on
// `app` directly since this isn't an Umbraco backoffice/website endpoint of its own.
// BlueprintsAdmin is self-registered by WayfinderUmbracoComposer (no wiring needed for the
// policy itself) — same policy ServiceBlueprintAuthoringController's REST surface already uses.
// Explicit AuthenticationSchemes is required here (confirmed live — without it, this endpoint
// challenges the app's *default* scheme, this reference app's own demo citizen/caseworker
// cookie, not Umbraco's backoffice one). ServiceBlueprintAuthoringController never needs this
// because Umbraco's Management API route group forces the backoffice scheme(s) for every
// controller under it; a bare minimal API mapped outside that grouping doesn't inherit that.
// The bearer-token scheme is "OpenIddict.Validation.AspNetCore", not
// Constants.Security.BackOfficeTokenAuthenticationType ("UmbracoBackOfficeToken") — confirmed
// live: that legacy constant has no registered handler in Umbraco 17 (it moved to OpenIddict's
// client-credentials grant against the Management API token endpoint — see
// docs/demos/licence-transfer-mcp-walkthrough.md's historical flow). The interactive backoffice
// cookie scheme is included too so a signed-in backoffice browser session can call it directly.
app.MapServiceBlueprintAuthoringMcp().RequireAuthorization(new AuthorizeAttribute
{
    Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin,
    AuthenticationSchemes = $"{Constants.Security.BackOfficeAuthenticationType},OpenIddict.Validation.AspNetCore",
});

await app.RunAsync();
