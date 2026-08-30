using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Drops the RFC 8707 <c>resource</c> parameter from an authorization or token request made by
/// the MCP OAuth client.
///
/// An MCP client (Claude Code, etc.) is required by the MCP spec to send
/// <c>resource=&lt;the MCP endpoint URL&gt;</c> on every authorization and token request, whether
/// or not the authorization server supports resource indicators. Umbraco's backoffice OpenIddict
/// server doesn't just ignore an unknown resource — it rejects the request outright with
/// <c>invalid_target</c> (<c>OpenIddict error ID2190</c>). Since this integration validates the
/// resulting opaque token locally via the <c>BlueprintsAdmin</c> policy rather than by audience
/// (the same way every other backoffice token is treated), the safe thing is to strip the
/// parameter and let the flow proceed — the spec-compliant behaviour for an AS that "doesn't
/// support the capability".
/// </summary>
/// <remarks>
/// Scoped to <see cref="WayfinderMcpOptions.ClientId"/> only — every other client's
/// <c>resource</c> parameter is left for Umbraco's own validation to handle. Registering the MCP
/// endpoint as a real OpenIddict scope/resource and validating audience end to end is the
/// hardening path, deferred: it needs the host's public base URL known at startup and matching
/// <c>AddAudiences(...)</c> on the validation side.
/// </remarks>
public sealed class WayfinderMcpDropResourceParameter(IOptions<WayfinderMcpOptions> options)
    : IOpenIddictServerHandler<ExtractAuthorizationRequestContext>,
      IOpenIddictServerHandler<ExtractTokenRequestContext>
{
    // Late in the Extract stage (which entirely precedes request validation), so the built-in
    // extractor has already populated context.Request before this runs.
    private const int HandlerOrder = int.MaxValue - 100_000;

    public static OpenIddictServerHandlerDescriptor AuthorizationRequestDescriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ExtractAuthorizationRequestContext>()
            .UseScopedHandler<WayfinderMcpDropResourceParameter>()
            .SetOrder(HandlerOrder)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public static OpenIddictServerHandlerDescriptor TokenRequestDescriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ExtractTokenRequestContext>()
            .UseScopedHandler<WayfinderMcpDropResourceParameter>()
            .SetOrder(HandlerOrder)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ExtractAuthorizationRequestContext context)
    {
        Strip(context.Request);
        return default;
    }

    public ValueTask HandleAsync(ExtractTokenRequestContext context)
    {
        Strip(context.Request);
        return default;
    }

    private void Strip(OpenIddictRequest? request)
    {
        if (request is not null
            && string.Equals(request.ClientId, options.Value.ClientId, StringComparison.Ordinal)
            && request.HasParameter(OpenIddictConstants.Parameters.Resource))
        {
            request.RemoveParameter(OpenIddictConstants.Parameters.Resource);
        }
    }
}
