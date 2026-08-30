using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Server.AspNetCore;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Extensions;

/// <summary>
/// Opt-in wiring for the one-click MCP OAuth flow: a connecting MCP client (Claude Code, etc.)
/// authenticates by logging into this site's Umbraco backoffice, rather than a human first
/// minting a short-lived bearer token by hand and pasting it as a header.
///
/// A bare <c>Wayfinder.Umbraco</c> package reference wires none of this — a host calls
/// <see cref="AddWayfinderUmbracoMcpAuthentication"/> from its own composer to turn it on.
/// </summary>
public static class WayfinderUmbracoMcpExtensions
{
    /// <summary>
    /// Registers the pre-configured public OpenIddict client the MCP OAuth flow uses (created and
    /// kept up to date at every startup by <see cref="WayfinderMcpOAuthClientInstaller"/>), binds
    /// <see cref="WayfinderMcpOptions"/> from the <c>Wayfinder:Mcp</c> configuration section, and
    /// — in the Development environment only — relaxes OpenIddict's HTTPS requirement so the flow
    /// works against a self-signed <c>localhost</c> certificate.
    /// </summary>
    /// <remarks>
    /// The host still maps the MCP endpoint and applies its own authorization policy (the
    /// reference app: <c>MapServiceBlueprintAuthoringMcp().RequireAuthorization(BlueprintsAdmin)</c>).
    /// This method only supplies the OAuth client + discovery so an MCP client can obtain a token
    /// interactively; token <em>validation</em> stays on Umbraco's existing
    /// <c>OpenIddict.Validation.AspNetCore</c> scheme, because backoffice access tokens are opaque
    /// reference tokens, not JWTs.
    /// </remarks>
    public static IUmbracoBuilder AddWayfinderUmbracoMcpAuthentication(
        this IUmbracoBuilder builder,
        Action<WayfinderMcpOptions>? configure = null)
    {
        builder.Services.AddOptions<WayfinderMcpOptions>()
            .BindConfiguration(WayfinderMcpOptions.SectionName)
            .ValidateOnStart();

        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<WayfinderMcpOptions>, WayfinderMcpOptionsValidator>());

        // Scoped, deliberately: IOpenIddictApplicationManager is scoped and DI validation rejects
        // a singleton that depends on it (see the installer's own remarks). One registration per
        // opt-in call is fine — Umbraco dispatches each notification to every registered handler,
        // and a second AddWayfinderUmbracoMcpAuthentication() call is a host mistake, not a
        // supported compose path.
        builder.Services.AddScoped<
            INotificationAsyncHandler<UmbracoApplicationStartedNotification>,
            WayfinderMcpOAuthClientInstaller>();

        // A self-signed localhost cert would otherwise make OpenIddict refuse to issue tokens
        // over the dev HTTPS endpoint. Never relaxed outside Development — gated on the injected
        // IHostEnvironment, not on a captured build-time flag.
        builder.Services.AddOptions<OpenIddictServerAspNetCoreOptions>()
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (environment.IsDevelopment())
                {
                    options.DisableTransportSecurityRequirement = true;
                }
            });

        return builder;
    }
}
