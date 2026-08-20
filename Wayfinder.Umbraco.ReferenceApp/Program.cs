using System.Security.Claims;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Rendering.GovUk;
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
    options.ResolveAccessProfile = ctx => ReferenceAppAuth.ResolveAccessProfile(ctx.User);
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

builder.Services.AddSingleton<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceContentSeeder>();
builder.Services.AddSingleton<INotificationAsyncHandler<UmbracoApplicationStartedNotification>, ReferenceBlueprintSeeder>();

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

await app.RunAsync();
