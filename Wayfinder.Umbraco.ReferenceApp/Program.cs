using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Extensions;
using Wayfinder.Umbraco.ReferenceApp;

var builder = WebApplication.CreateBuilder(args);

// Local secrets override — gitignored.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// A demo identity for the two actor lanes this reference app proves: the citizen-facing stage
// block (owner-restricted, one instance per visitor) and the caseworker-facing worklist block
// (a shared queue, real pickup/putback). Named scheme, not the ASP.NET Core default — Umbraco's
// own backoffice authentication owns that.
builder.Services
    .AddAuthentication(ReferenceAppAuth.SchemeName)
    .AddCookie(ReferenceAppAuth.SchemeName, options =>
    {
        options.LoginPath = "/demo/login";
        options.Cookie.Name = "WayfinderUmbracoReferenceApp";
    });
builder.Services.AddAuthorization();

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

builder.Services.AddSingleton<Umbraco.Cms.Core.Events.INotificationAsyncHandler<
    Umbraco.Cms.Core.Notifications.UmbracoApplicationStartedNotification>, ReferenceContentSeeder>();

var app = builder.Build();

// No explicit UseAuthentication()/UseAuthorization() here — Umbraco's own pipeline
// (UseUmbraco().WithMiddleware(...) below) already wires both in at the right point, the same
// way UmbracoPrism.Core's own PrismComposer never calls them directly either; only the DI
// registrations above (AddAuthentication/AddCookie/AddAuthorization) are this app's job.
ReferenceAppAuth.MapDemoLoginRoutes(app);

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
