using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Engine.Http;
using Wayfinder.Engine.Mcp;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco;
using Wayfinder.Umbraco.Extensions;
using Wayfinder.Umbraco.Mcp;
using Wayfinder.Umbraco.ReferenceApp;
using Wayfinder.Umbraco.Services;

var builder = WebApplication.CreateBuilder(args);

// The route the MCP-over-HTTP endpoint is mapped at — shared by the endpoint mapping, its OAuth
// discovery documents, and the 401-challenge middleware so they can't drift apart.
const string McpEndpointPath = "/wayfinder/service-blueprint-authoring/mcp";

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
    // Umbraco Automate (MIT) registers itself through its own UmbracoAutomateComposer, picked up
    // by AddComposers() below — a bare package reference is enough, no explicit AddUmbracoAutomate()
    // call (that would double-register WorkflowCore). The NJF Coaching Standards support system
    // (appsettings.json Wayfinder:SupportSystems) POSTs each invocation to an Automate webhook
    // automation on this same site; the automation does the work and calls back
    // /wayfinder/support-systems/callbacks (mapped below). Nothing Wayfinder ships knows about
    // Automate — it is a plain webhook consumer. See docs/automate-support-system-walkthrough.md.
    .AddComposers()
    // One-click MCP OAuth: an MCP client (Claude Code, etc.) connects by logging into this
    // site's Umbraco backoffice, rather than a human hand-minting a short-lived bearer token
    // and pasting it as a header. Registers a pre-configured public PKCE OpenIddict client
    // (default id "umbraco-back-office-wayfinder-mcp", loopback callback port 33418 in
    // Development) and, below, the discovery documents + 401 challenge hint the flow needs.
    // The manual client-credentials flow (README) still works for headless/CI agents.
    .AddWayfinderUmbracoMcpAuthentication()
    .Build();

builder.Services.AddSingleton<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceContentSeeder>();
builder.Services.AddSingleton<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceBlueprintSeeder>();
// Builds and publishes the "NJF Coaching Standards" Automate automation in code, so the
// config-only webhook support system has a real automation ready and waiting. A BackgroundService
// (not a startup notification) because it must run after Automate has created its default workspace.
builder.Services.AddHostedService<AutomateCoachingStandardsSeeder>();
// Scoped, not Singleton like the other two seeders — IBackOfficeUserClientCredentialsManager is
// itself registered Scoped by Umbraco, and DI validation fails fast on a Singleton consuming a
// Scoped dependency (confirmed live).
builder.Services.AddScoped<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceMcpDemoAgentSeeder>();

var app = builder.Build();

// Outermost middleware, deliberately: it post-processes the finished 401 that the MCP endpoint's
// own authorization produces (adding the RFC 9728 `resource_metadata` hint so an MCP client can
// start the OAuth flow). WebApplication auto-inserts UseAuthentication/UseAuthorization near the
// top of the pipeline for a RequireAuthorization endpoint, and UseAuthorization short-circuits a
// failure without calling downstream — so this only sees that 401 if it wraps the whole pipeline.
// Scoped to the MCP path; every other 401 on the site is left exactly as it was.
app.UseWayfinderUmbracoMcpAuthChallenge(McpEndpointPath);

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
// docs/demos/licence-transfer-mcp-walkthrough.md's historical flow). Backoffice access tokens
// are opaque reference tokens, not JWTs, so OpenIddict.Validation is what introspects them —
// interactive-OAuth tokens and headless client-credentials tokens alike. The interactive
// backoffice cookie scheme is included too so a signed-in backoffice browser session can call
// it directly.
app.MapServiceBlueprintAuthoringMcp(McpEndpointPath).RequireAuthorization(new AuthorizeAttribute
{
    Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin,
    AuthenticationSchemes = $"{Constants.Security.BackOfficeAuthenticationType},OpenIddict.Validation.AspNetCore",
});

// The OAuth discovery documents an MCP client walks from the 401 challenge above:
// /.well-known/oauth-protected-resource (this endpoint), plus an RFC 8414 authorization-server
// metadata document standing in for Umbraco's backoffice OpenIddict server, which publishes none.
app.MapWayfinderUmbracoMcpOAuthDiscovery(McpEndpointPath);

// The inbound half of the config-only webhook support system: the callback the NJF Coaching
// Standards Automate automation posts to resolve a waiting registrar cursor. AllowAnonymous is
// deliberate and required — this is a server-to-server webhook, not a browser/backoffice call,
// so it must not challenge the demo cookie or the backoffice scheme. Its own gate is the
// X-Webhook-Secret header when NJF_STANDARDS_CALLBACK_SECRET is set (user-secrets / the AppHost
// supplies it); with no secret set it logs a warning and trusts the loopback network, matching
// this reference app's documented minimal-auth posture.
app.MapWebhookSupportSystemCallbacks(
        () => app.Services.GetRequiredService<UmbracoProcessManagerEngine>(),
        sharedSecret: builder.Configuration["NJF_STANDARDS_CALLBACK_SECRET"])
    .AllowAnonymous();

await app.RunAsync();
