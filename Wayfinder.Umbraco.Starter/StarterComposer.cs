using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Mcp;
using Wayfinder.Umbraco.Extensions;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Starter;

/// <summary>
/// Everything a host's <c>Program.cs</c> would otherwise wire, so a stock <c>dotnet new umbraco</c>
/// site becomes a working Wayfinder demo from the package reference alone: identity for the public
/// and the demo caseworker, the MCP authoring endpoint with backoffice sign-in, and the first-boot
/// content (<see cref="StarterSeeder"/>).
/// </summary>
[ComposeAfter(typeof(WayfinderUmbracoComposer))]
public class StarterComposer : IComposer
{
    public const string McpPath = "/wayfinder/service-blueprint-authoring/mcp";

    public const string TenantId = "starter";

    public void Compose(IUmbracoBuilder builder)
    {
        // Demo sign-in has no password, so a starter site must never be reachable as a real one.
        // Checked against the injected IHostEnvironment when the host starts.
        builder.Services.AddOptions<StarterOptions>()
            .BindConfiguration(StarterOptions.SectionName)
            .Validate<IHostEnvironment>(
                StarterOptions.MayRun,
                "Wayfinder.Umbraco.Starter signs anyone in as a caseworker without a password, so it only runs in the " +
                "Development environment. Remove the package before deploying, or set Wayfinder:Starter:AllowOutsideDevelopment " +
                "to true for a demo server that holds nothing real.")
            .ValidateOnStart();

        builder.Services.AddWayfinderUmbraco(options =>
        {
            options.ResolveTenantId = _ => TenantId;
            options.ResolveAccessProfile = (context, _) => Task.FromResult(StarterAccess.Resolve(
                context.User,
                context.RequestServices.GetRequiredService<IProcessManager>().GetAllDefinitions()));
        });

        builder.Services.AddWayfinderVisitors(visitors =>
        {
            visitors.SignedInUserId = context => context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            visitors.StaffLandingPath = StarterRoutes.WorkQueue;
            visitors.ExpiredLinkPath = "/";
        });

        // The front end's default scheme, set in a composer because AddBackOffice() registers
        // Umbraco's own defaults first and the last registration wins. All three are set: Umbraco
        // sets DefaultAuthenticateScheme explicitly, which DefaultScheme alone would not override.
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = StarterPersonas.SchemeName;
                options.DefaultSignInScheme = StarterPersonas.SchemeName;
                options.DefaultChallengeScheme = StarterPersonas.SchemeName;
            })
            .AddCookie(StarterPersonas.SchemeName, options =>
            {
                options.LoginPath = StarterRoutes.WorkQueue;
                options.Cookie.Name = "WayfinderStarter";
            });

        builder.Services.AddServiceBlueprintAuthoringMcp(StarterGuidance.McpInstructions);
        builder.AddWayfinderUmbracoMcpAuthentication();

        builder.Services.Configure<UmbracoPipelineOptions>(options => options.AddFilter(new UmbracoPipelineFilter("Wayfinder.Umbraco.Starter")
        {
            // First in Umbraco's pipeline, ahead of its UseAuthorization, so it sees the MCP
            // endpoint's 401 and adds the hint an MCP client follows to sign in.
            PrePipeline = app => app.UseWayfinderUmbracoMcpAuthChallenge(McpPath),
            Endpoints = app => app.UseEndpoints(endpoints =>
            {
                // The schemes are explicit: a minimal API outside Umbraco's Management API would
                // otherwise challenge the starter's caseworker cookie, not the backoffice.
                endpoints.MapServiceBlueprintAuthoringMcp(McpPath).RequireAuthorization(new AuthorizeAttribute
                {
                    Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin,
                    AuthenticationSchemes = $"{Constants.Security.BackOfficeAuthenticationType},OpenIddict.Validation.AspNetCore",
                });
                endpoints.MapWayfinderUmbracoMcpOAuthDiscovery(McpPath);
            }),
        }));

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, StarterSeeder>();
    }
}
