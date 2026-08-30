using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Registers (or updates, idempotently) the single public OpenIddict client the MCP OAuth flow
/// uses, on every startup — the same "pre-register a public, PKCE-only Authorization Code client
/// programmatically because the backoffice UI can't create one" approach Umbraco HQ's own hosted
/// MCP worker uses. Wired only when a host opts in via
/// <see cref="Extensions.WayfinderUmbracoMcpExtensions.AddWayfinderUmbracoMcpAuthentication"/>.
/// </summary>
/// <remarks>
/// Registered <c>Scoped</c> (not through <c>IUmbracoBuilder.AddNotificationAsyncHandler</c>)
/// because <see cref="IOpenIddictApplicationManager"/> is itself scoped and DI validation fails
/// fast on a singleton consuming it — the same reason
/// <c>Wayfinder.Umbraco.ReferenceApp</c>'s own MCP demo-agent seeder is registered scoped.
/// </remarks>
public sealed class WayfinderMcpOAuthClientInstaller(
    IOpenIddictApplicationManager applicationManager,
    IOptions<WayfinderMcpOptions> options,
    IHostEnvironment environment,
    IRuntimeState runtimeState,
    ILogger<WayfinderMcpOAuthClientInstaller> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        var descriptor = BuildDescriptor(options.Value, environment);

        var existing = await applicationManager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
        if (existing is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
            logger.LogInformation(
                "WAYFINDER MCP OAUTH: registered OpenIddict client {ClientId} with {RedirectUriCount} redirect URI(s).",
                descriptor.ClientId, descriptor.RedirectUris.Count);
            return;
        }

        // Always update — redirect URIs, permissions and display name can all change between
        // deployments, and the client id is stable so this converges rather than duplicates.
        await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
        logger.LogInformation(
            "WAYFINDER MCP OAUTH: updated OpenIddict client {ClientId} ({RedirectUriCount} redirect URI(s)).",
            descriptor.ClientId, descriptor.RedirectUris.Count);
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(WayfinderMcpOptions options, IHostEnvironment environment)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.ClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = options.DisplayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
        };

        foreach (var uri in EnumerateRedirectUris(options, environment))
        {
            descriptor.RedirectUris.Add(uri);
        }

        foreach (var uri in options.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        return descriptor;
    }

    private static IEnumerable<Uri> EnumerateRedirectUris(WayfinderMcpOptions options, IHostEnvironment environment)
    {
        foreach (var uri in options.RedirectUris)
        {
            yield return new Uri(uri, UriKind.Absolute);
        }

        // A loopback HTTP callback is only ever registered for a Development host — never for a
        // real deployment, where it would be a standing open redirect target on plain HTTP.
        if (!environment.IsDevelopment())
        {
            yield break;
        }

        foreach (var port in options.LocalCallbackPorts)
        {
            yield return new Uri($"http://localhost:{port}/callback", UriKind.Absolute);
            yield return new Uri($"http://127.0.0.1:{port}/callback", UriKind.Absolute);
        }
    }
}
