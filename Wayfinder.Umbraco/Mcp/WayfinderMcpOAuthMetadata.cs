using System.Text.Json.Serialization;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// OAuth 2.0 Authorization Server Metadata (RFC 8414) describing Umbraco's backoffice OpenIddict
/// server — which publishes no discovery document of its own. Its <c>issuer</c> is the site root
/// (<c>https://host/</c>): that is the value the backoffice OpenIddict server stamps into the
/// <c>iss</c> authorization-response parameter (RFC 9207), so an MCP client that checks the two
/// match will only accept the real one. Served by <see cref="WayfinderMcpDiscoveryEndpoints"/>
/// at the root <c>/.well-known/oauth-authorization-server</c> — which is free; the separate
/// Delivery-API "member" OpenIddict server owns only <c>/.well-known/openid-configuration</c>,
/// and RFC 8414 clients try <c>oauth-authorization-server</c> first.
/// </summary>
internal sealed class WayfinderMcpAuthorizationServerMetadata
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("revocation_endpoint")]
    public required string RevocationEndpoint { get; init; }

    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    [JsonPropertyName("response_types_supported")]
    public string[] ResponseTypesSupported { get; init; } = ["code"];

    [JsonPropertyName("response_modes_supported")]
    public string[] ResponseModesSupported { get; init; } = ["query"];

    [JsonPropertyName("grant_types_supported")]
    public string[] GrantTypesSupported { get; init; } = ["authorization_code", "refresh_token"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public string[] CodeChallengeMethodsSupported { get; init; } = ["S256"];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public string[] TokenEndpointAuthMethodsSupported { get; init; } = ["none"];

    [JsonPropertyName("scopes_supported")]
    public string[] ScopesSupported { get; init; } = ["openid", "offline_access"];
}

/// <summary>
/// OAuth 2.0 Protected Resource Metadata (RFC 9728) for this host's MCP endpoint — the document
/// an MCP client fetches (via the <c>resource_metadata</c> hint in a 401 <c>WWW-Authenticate</c>
/// header) to discover which authorization server to use.
/// </summary>
internal sealed class WayfinderMcpProtectedResourceMetadata
{
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    [JsonPropertyName("authorization_servers")]
    public required string[] AuthorizationServers { get; init; }

    [JsonPropertyName("scopes_supported")]
    public string[] ScopesSupported { get; init; } = ["openid", "offline_access"];

    [JsonPropertyName("bearer_methods_supported")]
    public string[] BearerMethodsSupported { get; init; } = ["header"];

    [JsonPropertyName("resource_documentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourceDocumentation { get; init; }
}

/// <summary>
/// Builds the two discovery documents <see cref="WayfinderMcpDiscoveryEndpoints"/> serves.
/// Umbraco's backoffice OpenIddict endpoint paths are stable constants here, confirmed against a
/// live Umbraco 17 install; the observable behaviour is covered by
/// <c>WayfinderMcpDiscoveryEndpointsTests</c> walking the real HTTP routes.
/// </summary>
internal static class WayfinderMcpOAuthMetadata
{
    /// <summary>Umbraco 17's backoffice OpenIddict authorization endpoint, relative to the site root.</summary>
    public const string BackOfficeAuthorizationPath = "/umbraco/management/api/v1/security/back-office/authorize";

    /// <summary>Umbraco 17's backoffice OpenIddict token endpoint, relative to the site root.</summary>
    public const string BackOfficeTokenPath = "/umbraco/management/api/v1/security/back-office/token";

    /// <summary>Umbraco 17's backoffice OpenIddict revocation endpoint, relative to the site root.</summary>
    public const string BackOfficeRevocationPath = "/umbraco/management/api/v1/security/back-office/revoke";

    /// <summary>OpenIddict's shared JWKS document, relative to the site root.</summary>
    public const string JwksPath = "/.well-known/jwks";

    /// <param name="siteRootUri">
    /// Absolute site root the current request came in on (scheme + host + optional port), no
    /// trailing slash — e.g. <c>https://example.test</c>. Everything else is derived from it so
    /// the documents are correct behind any host name, port or reverse proxy.
    /// </param>
    public static WayfinderMcpAuthorizationServerMetadata BuildAuthorizationServerMetadata(
        string siteRootUri) => new()
    {
        // The backoffice OpenIddict server's issuer IS the site root, trailing slash and all —
        // that is exactly what it puts in the RFC 9207 `iss` authorization-response parameter,
        // which an MCP client compares against this value.
        Issuer = siteRootUri + "/",
        AuthorizationEndpoint = siteRootUri + BackOfficeAuthorizationPath,
        TokenEndpoint = siteRootUri + BackOfficeTokenPath,
        RevocationEndpoint = siteRootUri + BackOfficeRevocationPath,
        JwksUri = siteRootUri + JwksPath,
    };

    public static WayfinderMcpProtectedResourceMetadata BuildProtectedResourceMetadata(
        string siteRootUri, string mcpResourcePath, WayfinderMcpOptions options) => new()
    {
        // Canonical resource URI per RFC 8707 / RFC 9728: no trailing slash.
        Resource = siteRootUri + mcpResourcePath.TrimEnd('/'),
        AuthorizationServers = [siteRootUri + "/"],
        ResourceDocumentation = options.ResourceDocumentationUrl,
    };
}
