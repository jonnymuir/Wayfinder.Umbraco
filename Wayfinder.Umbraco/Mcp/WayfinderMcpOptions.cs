namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Options for the one-click OAuth flow that lets an MCP client (Claude Code, etc.) connect to
/// this host's service-blueprint-authoring MCP endpoint by logging into the Umbraco backoffice,
/// instead of the manual "create an API user, register client credentials, exchange a short-lived
/// token, pass it as a header" dance.
///
/// Bind from configuration section <c>Wayfinder:Mcp</c>. Only takes effect when a host calls
/// <see cref="Extensions.WayfinderUmbracoMcpExtensions.AddWayfinderUmbracoMcpAuthentication"/> —
/// a bare package reference registers nothing here.
/// </summary>
/// <remarks>
/// Models Umbraco HQ's own hosted-MCP OAuth setup
/// (docs.umbraco.com/umbraco-in-ai/mcp/base-mcp/hosted-mcp/umbraco-setup): a single
/// <see cref="global::OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Public"/>
/// (PKCE, no secret) OpenIddict client, pre-registered at startup, that the backoffice's own
/// OpenIddict server issues authorization-code + refresh tokens for after an interactive
/// backoffice login. Nothing here is dynamic client registration — the client id is fixed and a
/// connecting MCP client passes it explicitly (Claude Code: <c>--client-id</c>).
/// </remarks>
public sealed class WayfinderMcpOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Wayfinder:Mcp";

    /// <summary>
    /// The OpenIddict client id registered for the MCP OAuth flow. A connecting MCP client must
    /// pass this same value (Claude Code: <c>claude mcp add --transport http … --client-id
    /// umbraco-back-office-wayfinder-mcp</c>). The <c>umbraco-back-office-</c> prefix matches the
    /// namespace Umbraco itself uses for backoffice OpenIddict clients (HQ's own MCP client is
    /// <c>umbraco-back-office-mcp</c>).
    /// </summary>
    public string ClientId { get; set; } = "umbraco-back-office-wayfinder-mcp";

    /// <summary>Human-readable name shown on the OpenIddict consent screen.</summary>
    public string DisplayName { get; set; } = "Wayfinder — service blueprint authoring (MCP)";

    /// <summary>
    /// Absolute redirect URIs (OAuth callback) permitted for this client, on top of the local
    /// development ones derived from <see cref="LocalCallbackPorts"/>. A real deployment adds the
    /// callback URL(s) its MCP client(s) use here, e.g.
    /// <c>https://claude.ai/api/mcp/auth_callback</c> or a fixed
    /// <c>http://localhost:33418/callback</c> for a developer machine that isn't running in the
    /// Development environment.
    /// </summary>
    public string[] RedirectUris { get; set; } = [];

    /// <summary>
    /// Absolute post-logout redirect URIs permitted for this client. Usually left empty — an MCP
    /// client rarely drives an interactive sign-out.
    /// </summary>
    public string[] PostLogoutRedirectUris { get; set; } = [];

    /// <summary>
    /// Loopback ports for which <c>http://localhost:{port}/callback</c> and
    /// <c>http://127.0.0.1:{port}/callback</c> are added as permitted redirect URIs — but only
    /// when the app is running in the Development environment (a loopback HTTP redirect is never
    /// registered for a non-Development host). Claude Code picks a random callback port unless
    /// told otherwise, so a connecting developer passes <c>--callback-port 33418</c> to match the
    /// default here.
    /// </summary>
    public int[] LocalCallbackPorts { get; set; } = [33418];

    /// <summary>
    /// URL of human-readable documentation for this protected resource, surfaced in the
    /// OAuth 2.0 Protected Resource Metadata document (RFC 9728) an MCP client discovers. Null
    /// omits the field.
    /// </summary>
    public string? ResourceDocumentationUrl { get; set; }

    /// <summary>
    /// Route prefix (relative to the site root, no trailing slash) under which the OAuth
    /// discovery documents this package serves on Umbraco's behalf are mounted — Umbraco's
    /// backoffice OpenIddict server publishes no discovery metadata of its own, so an MCP client
    /// can't find its <c>authorization_endpoint</c>/<c>token_endpoint</c> without this shim. The
    /// path-scoped issuer this produces (<c>{SiteRoot}{DiscoveryPathPrefix}</c>) deliberately
    /// avoids the site root, where Umbraco's <em>member</em> (Delivery API) OpenIddict server
    /// already owns <c>/.well-known/openid-configuration</c>.
    /// </summary>
    public string DiscoveryPathPrefix { get; set; } = "/wayfinder/mcp-auth";
}
