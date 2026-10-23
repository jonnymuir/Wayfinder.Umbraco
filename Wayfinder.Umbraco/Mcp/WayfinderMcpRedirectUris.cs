using Microsoft.Extensions.Hosting;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// The OAuth callbacks the MCP client accepts: every configured <see cref="WayfinderMcpOptions.RedirectUris"/>
/// entry and, in Development only, the loopback callbacks local MCP clients listen on for each
/// <see cref="WayfinderMcpOptions.LocalCallbackPorts"/> entry. Claude Code listens on
/// <c>http://localhost:{port}/callback</c>; VS Code on <c>http://127.0.0.1:33418/</c>.
/// </summary>
public static class WayfinderMcpRedirectUris
{
    public static IReadOnlyList<Uri> For(WayfinderMcpOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var uris = options.RedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)).ToList();

        // A loopback HTTP callback is only ever registered for a Development host, never for a
        // real deployment, where it would be a standing open redirect target on plain HTTP.
        if (environment.IsDevelopment())
        {
            foreach (var port in options.LocalCallbackPorts)
            {
                uris.Add(new Uri($"http://localhost:{port}/callback", UriKind.Absolute));
                uris.Add(new Uri($"http://127.0.0.1:{port}/callback", UriKind.Absolute));
                uris.Add(new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute));
            }
        }

        return uris.DistinctBy(uri => uri.AbsoluteUri).ToArray();
    }
}
