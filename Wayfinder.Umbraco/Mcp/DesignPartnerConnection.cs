namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Everything an MCP client needs to connect to this site as a design partner, for the backoffice's
/// "Connect a design partner" panel. <see cref="Clients"/> says, for each MCP client the panel
/// gives setup for, which sign-in callbacks it needs that this site doesn't accept yet.
/// </summary>
public sealed record DesignPartnerConnection(
    string ServerName,
    string ServerUrl,
    string ClientId,
    int CallbackPort,
    bool Development,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<DesignPartnerClientSetup> Clients)
{
    /// <summary>The name the setup gives the MCP server, so its tools read <c>mcp__wayfinder-umbraco__…</c>.</summary>
    public const string DefaultServerName = "wayfinder-umbraco";

    /// <summary>Where Claude Desktop and claude.ai custom connectors return after sign-in.</summary>
    public const string ClaudeConnectorCallback = "https://claude.ai/api/mcp/auth_callback";

    /// <summary>The loopback callback VS Code listens on for MCP sign-in.</summary>
    public const string VsCodeCallback = "http://127.0.0.1:33418/";

    public static DesignPartnerConnection Describe(
        Uri siteRoot, string mcpPath, WayfinderMcpOptions options, IReadOnlyList<Uri> redirectUris, bool development)
    {
        ArgumentNullException.ThrowIfNull(siteRoot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(redirectUris);

        var accepted = redirectUris.Select(uri => uri.AbsoluteUri).ToArray();
        var callbackPort = ClaudeCodeCallbackPort(redirectUris) ?? options.LocalCallbackPorts.FirstOrDefault(33418);

        return new DesignPartnerConnection(
            DefaultServerName,
            new Uri(siteRoot, mcpPath).AbsoluteUri,
            options.ClientId,
            callbackPort,
            development,
            accepted,
            [
                Setup("claude-code", accepted, $"http://localhost:{callbackPort}/callback"),
                Setup("claude-desktop", accepted, ClaudeConnectorCallback),
                Setup("vscode", accepted, VsCodeCallback),
                // Copilot CLI listens on a port of its own choosing; any port on a registered
                // 127.0.0.1 callback is accepted (WayfinderMcpLoopbackRedirectUri).
                Setup("github-copilot", accepted, VsCodeCallback),
            ]);
    }

    private static DesignPartnerClientSetup Setup(string client, string[] accepted, params string[] needs) =>
        new(client, needs.Where(uri => !accepted.Contains(uri, StringComparer.OrdinalIgnoreCase)).ToArray());

    private static int? ClaudeCodeCallbackPort(IReadOnlyList<Uri> redirectUris) =>
        redirectUris.FirstOrDefault(uri => uri.Scheme == Uri.UriSchemeHttp && uri.Host == "localhost" && uri.AbsolutePath == "/callback")?.Port;
}

/// <summary>
/// One MCP client's setup. It is ready when <see cref="MissingRedirectUris"/> is empty; otherwise
/// the host adds those to <c>Wayfinder:Mcp:RedirectUris</c>.
/// </summary>
public sealed record DesignPartnerClientSetup(string Client, IReadOnlyList<string> MissingRedirectUris)
{
    public bool Ready => MissingRedirectUris.Count == 0;
}
