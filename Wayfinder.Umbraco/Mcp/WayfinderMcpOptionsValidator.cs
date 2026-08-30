using Microsoft.Extensions.Options;

namespace Wayfinder.Umbraco.Mcp;

/// <summary>
/// Fails startup fast (paired with <c>ValidateOnStart()</c>) on a misconfigured
/// <c>Wayfinder:Mcp</c> section, rather than registering a broken OpenIddict client and only
/// discovering it when an MCP client's OAuth flow mysteriously 400s.
/// </summary>
public sealed class WayfinderMcpOptionsValidator : IValidateOptions<WayfinderMcpOptions>
{
    public ValidateOptionsResult Validate(string? name, WayfinderMcpOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{nameof(WayfinderMcpOptions.ClientId)} must not be empty.");
        }

        foreach (var (value, label) in Absolutes(options))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add($"{label} entry '{value}' is not an absolute http(s) URL.");
            }
        }

        foreach (var port in options.LocalCallbackPorts)
        {
            if (port is < 1 or > 65535)
            {
                failures.Add($"{nameof(WayfinderMcpOptions.LocalCallbackPorts)} entry '{port}' is not a valid TCP port.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<(string Value, string Label)> Absolutes(WayfinderMcpOptions options)
    {
        foreach (var uri in options.RedirectUris)
        {
            yield return (uri, nameof(WayfinderMcpOptions.RedirectUris));
        }

        foreach (var uri in options.PostLogoutRedirectUris)
        {
            yield return (uri, nameof(WayfinderMcpOptions.PostLogoutRedirectUris));
        }

        if (options.ResourceDocumentationUrl is { } doc)
        {
            yield return (doc, nameof(WayfinderMcpOptions.ResourceDocumentationUrl));
        }
    }
}
