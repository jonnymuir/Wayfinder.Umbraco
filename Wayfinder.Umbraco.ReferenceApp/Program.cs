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

// The NJF Coaching Standards webhook is always HMAC-signed (appsettings.json). The AppHost
// supplies a per-run signing key; a bare `dotnet run` gets an ephemeral one here so the config
// -driven client and the seeded Automate trigger both have a key to agree on, with nothing
// secret in committed config.
builder.Configuration["NJF_STANDARDS_SIGNING_KEY"] ??=
    Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

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

// Security response headers for the Wayfinder-rendered GOV.UK front-end (home, /demo/login, the
// service pages with the citizen/worklist blocks). First in the pipeline so it also covers the
// _content/* static assets. A real host would centralise this (its own middleware, a CDN, a
// gateway); this reference app carries it inline to show the minimum a Wayfinder.Umbraco host
// should set, and so the estate's DAST baseline (.github/workflows/dast.yml) scans a
// representative target.
//
// Scoped to skip /umbraco* — the backoffice is a separate app surface (a Lit web-component SPA
// with dynamic imports) with its own header and CSP regime that Umbraco owns; a strict CSP there
// would break it. Umbraco already sets its own anti-clickjacking header on the backoffice.
//
// CSP matches the core repo's Wayfinder.ReferenceApp — the rendering stack
// (Wayfinder.Rendering.GovUk) is shared:
//   - script-src: 'self' for the vendored govuk-frontend / wayfinder JS under /_content/… (incl.
//     wayfinder-poll.js, which _Stage-Waiting.cshtml now loads externally rather than inlining),
//     plus one sha256 — no 'unsafe-inline', no 'unsafe-eval':
//       * GUQ5ad8… — GOV.UK Frontend's inline "js-enabled" bootstrap in ReferenceAppPageShell.cs.
//   - style-src: 'unsafe-inline' is required for the two inline style="…" attributes on the
//     signed-in nav in ReferenceAppPageShell.cs, plus Umbraco's Block Grid layout partials which
//     emit inline style="--umb-block-grid-…" custom properties on every seeded page.
//   - img-src data:: govuk-frontend's inline SVG data URIs.
// No HSTS: plain HTTP in Development (a TLS deployment adds app.UseHsts()). COEP omitted — it
// only matters for cross-origin isolation, which this host does not use.
const string contentSecurityPolicy =
    "default-src 'self'; " +
    "script-src 'self' 'sha256-GUQ5ad8JK5KmEWmROf3LZd9ge94daqNvd8xy9YS1iDw='; " +
    "style-src 'self' 'unsafe-inline'; " +
    "img-src 'self' data:; " +
    "font-src 'self'; " +
    "connect-src 'self'; " +
    "form-action 'self'; " +
    "frame-ancestors 'none'; " +
    "base-uri 'self'; " +
    "object-src 'none'";

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/umbraco"))
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = contentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] =
            "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), " +
            "fullscreen=(self), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), " +
            "midi=(), payment=(), picture-in-picture=(), publickey-credentials-get=(), " +
            "screen-wake-lock=(), sync-xhr=(), usb=(), xr-spatial-tracking=()";
    }

    await next();
});

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
