using System.Net;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Authentication;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Takes the place of OpenIddict's own redirect_uri check so the MCP client gets the loopback
/// rule RFC 8252 section 7.3 requires of an authorization server: a native client that listens on
/// a loopback IP address may use any port at the time of the request, because it takes whatever
/// port the operating system gives it. GitHub Copilot CLI does exactly that, so an exact match
/// against the registered <c>http://127.0.0.1:33418/</c> refuses it.
/// </summary>
/// <remarks>
/// Only for the MCP client, and only when the requested URI matches a registered loopback IP
/// redirect URI (<c>127.0.0.1</c> or <c>[::1]</c>, plain http) in everything but the port. The
/// requested URI must already be in canonical form, because OpenIddict redirects to it as sent:
/// <c>127.1</c>, dot segments or stray whitespace fall through to the exact match and fail.
/// <c>localhost</c> gets no such leniency: RFC 8252 section 8.3 recommends against it, and the
/// port rule is defined for loopback IP literals. Every other request, for every other client, goes
/// to OpenIddict's own handler unchanged. Loopback callbacks are registered for a Development host
/// only (<see cref="WayfinderMcpRedirectUris"/>), so a deployed site is unaffected unless a host
/// registers one itself.
/// </remarks>
public sealed class WayfinderMcpLoopbackRedirectUri(
    IOpenIddictApplicationManager applicationManager,
    IOptions<WayfinderMcpOptions> options)
    : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .AddFilter<OpenIddictServerHandlerFilters.RequireDegradedModeDisabled>()
            .UseScopedHandler<WayfinderMcpLoopbackRedirectUri>()
            .SetOrder(ValidateClientRedirectUri.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    private readonly ValidateClientRedirectUri _openIddict = new(applicationManager);

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        if (string.Equals(context.ClientId, options.Value.ClientId, StringComparison.Ordinal)
            && LoopbackIp(context.RedirectUri) is { } requested
            && string.Equals(context.RedirectUri, requested.AbsoluteUri, StringComparison.Ordinal)
            && await applicationManager.FindByClientIdAsync(context.ClientId!) is { } application)
        {
            foreach (var registered in await applicationManager.GetRedirectUrisAsync(application))
            {
                if (LoopbackIp(registered) is { } allowed && SameApartFromPort(requested, allowed))
                {
                    return;
                }
            }
        }

        await _openIddict.HandleAsync(context);
    }

    private static Uri? LoopbackIp(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
        && IPAddress.IsLoopback(address)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
            ? uri
            : null;

    private static bool SameApartFromPort(Uri requested, Uri allowed) =>
        string.Equals(
            new UriBuilder(requested) { Port = allowed.Port }.Uri.AbsoluteUri,
            allowed.AbsoluteUri,
            StringComparison.Ordinal);
}
