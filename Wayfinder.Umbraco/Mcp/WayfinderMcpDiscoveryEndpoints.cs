using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Maps the OAuth discovery documents an MCP client needs to connect to this host's
/// service-blueprint-authoring endpoint by logging into the Umbraco backoffice:
/// <list type="bullet">
///   <item>the Protected Resource Metadata (RFC 9728) for the MCP endpoint, and</item>
///   <item>an Authorization Server Metadata document (RFC 8414) standing in for Umbraco's
///   backoffice OpenIddict server, which publishes none of its own.</item>
/// </list>
/// All documents are built per-request from the incoming scheme/host, so they stay correct
/// behind any host name, port or reverse proxy. Every route is anonymous.
/// </summary>
public static class WayfinderMcpDiscoveryEndpoints
{
    /// <param name="mcpResourcePath">
    /// Root-relative path the MCP endpoint itself is mapped at (e.g.
    /// <c>/wayfinder/service-blueprint-authoring/mcp</c>) — the resource these documents describe.
    /// </param>
    public static IEndpointRouteBuilder MapWayfinderUmbracoMcpOAuthDiscovery(
        this IEndpointRouteBuilder endpoints, string mcpResourcePath)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<WayfinderMcpOptions>>().Value;

        // RFC 9728: Protected Resource Metadata. Both the bare well-known path and the
        // path-scoped form (for the resource whose own path is mcpResourcePath) — different MCP
        // clients ask for different ones.
        IResult ProtectedResourceMetadata(HttpRequest request) =>
            Results.Json(WayfinderMcpOAuthMetadata.BuildProtectedResourceMetadata(
                SiteRoot(request), mcpResourcePath, options));

        endpoints.MapGet("/.well-known/oauth-protected-resource", ProtectedResourceMetadata).AllowAnonymous();
        endpoints.MapGet($"/.well-known/oauth-protected-resource{mcpResourcePath}", ProtectedResourceMetadata).AllowAnonymous();

        // RFC 8414: Authorization Server Metadata for the backoffice OpenIddict server, whose
        // issuer is the site root. That makes the metadata location the root
        // "/.well-known/oauth-authorization-server" — free here; the Delivery-API "member"
        // server owns only the sibling "/.well-known/openid-configuration", which we leave to it
        // (RFC 8414 clients try "oauth-authorization-server" first).
        endpoints.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) =>
            Results.Json(WayfinderMcpOAuthMetadata.BuildAuthorizationServerMetadata(SiteRoot(request)))).AllowAnonymous();

        return endpoints;
    }

    private static string SiteRoot(HttpRequest request) => $"{request.Scheme}://{request.Host.Value}";
}
