using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Adds the <c>resource_metadata</c> hint (RFC 9728 §5.1) to the <c>WWW-Authenticate</c> header
/// on a <c>401</c> from the MCP endpoint, so an unauthenticated MCP client knows where to fetch
/// this resource's metadata and start the OAuth flow. Umbraco's own
/// <c>OpenIddict.Validation.AspNetCore</c> emits a bare <c>Bearer</c> challenge without it.
/// Scoped to the MCP endpoint path — every other 401 on the site is left exactly as it was.
/// </summary>
public sealed class WayfinderMcpChallengeMiddleware(RequestDelegate next, string mcpResourcePath)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.HasStarted
            || context.Response.StatusCode != StatusCodes.Status401Unauthorized
            || !context.Request.Path.StartsWithSegments(mcpResourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var metadataUrl = $"{context.Request.Scheme}://{context.Request.Host.Value}/.well-known/oauth-protected-resource";
        var existing = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();

        if (existing.Contains("resource_metadata=", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        context.Response.Headers[HeaderNames.WWWAuthenticate] = string.IsNullOrEmpty(existing)
            ? $"Bearer resource_metadata=\"{metadataUrl}\""
            : $"{existing.TrimEnd(',', ' ')}, resource_metadata=\"{metadataUrl}\"";
    }
}

/// <summary>Pipeline wiring for <see cref="WayfinderMcpChallengeMiddleware"/>.</summary>
public static class WayfinderMcpChallengeMiddlewareExtensions
{
    /// <param name="mcpResourcePath">
    /// Root-relative path the MCP endpoint is mapped at — the same value passed to
    /// <see cref="WayfinderMcpDiscoveryEndpoints.MapWayfinderUmbracoMcpOAuthDiscovery"/>.
    /// </param>
    public static IApplicationBuilder UseWayfinderUmbracoMcpAuthChallenge(
        this IApplicationBuilder app, string mcpResourcePath) =>
        app.UseMiddleware<WayfinderMcpChallengeMiddleware>(mcpResourcePath);
}
