using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Wayfinder.Umbraco.Mcp;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Wayfinder.Umbraco.Tests.Mcp;

/// <summary>
/// SECURITY REGRESSION. The MCP client may come back to any port on a registered loopback IP
/// callback, as RFC 8252 section 7.3 requires (GitHub Copilot CLI listens on an ephemeral port).
/// Nothing else is loosened: a different path, host, scheme or client, or <c>localhost</c>, still
/// needs an exact match, and so does any URI not written in canonical form, because OpenIddict
/// redirects to the URI exactly as it was sent.
/// </summary>
public class WayfinderMcpLoopbackRedirectUriTests
{
    private const string McpClientId = "umbraco-back-office-wayfinder-mcp";

    private static readonly string[] Registered =
    [
        "http://localhost:33418/callback",
        "http://127.0.0.1:33418/callback",
        "http://127.0.0.1:33418/",
    ];

    [Theory]
    [InlineData("http://127.0.0.1:61566/")]
    [InlineData("http://127.0.0.1:33418/")]
    [InlineData("http://127.0.0.1:50123/callback")]
    public async Task TheMcpClient_MayUseAnyPortOnARegisteredLoopbackIpCallback(string redirectUri)
    {
        var context = await Validate(McpClientId, redirectUri);

        context.IsRejected.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1:61566/elsewhere")]
    [InlineData("http://127.0.0.1:61566/?next=/umbraco")]
    [InlineData("http://localhost:61566/callback")]
    [InlineData("https://127.0.0.1:61566/")]
    [InlineData("http://[::1]:61566/")]
    [InlineData("http://127.0.0.2.example:61566/")]
    [InlineData("http://evil.example:33418/")]
    [InlineData("http://127.0.0.1:61566")]
    [InlineData("http://127.1:61566/")]
    [InlineData("http://0x7f000001:61566/")]
    [InlineData("http://2130706433:61566/")]
    [InlineData("HTTP://127.0.0.1:61566/")]
    [InlineData("http://127.0.0.1:061566/")]
    [InlineData("http://127.0.0.1:61566/x/../")]
    [InlineData("http://127.0.0.1:61566/%2e%2e/")]
    [InlineData("http://127.0.0.1:61566/\n")]
    [InlineData(" http://127.0.0.1:61566/")]
    [InlineData("http://127.0.0.1:61566\\@evil.example/")]
    [InlineData("http://evil.example@127.0.0.1:61566/")]
    public async Task AnythingButThePortStillHasToMatch(string redirectUri)
    {
        var context = await Validate(McpClientId, redirectUri);

        context.IsRejected.Should().BeTrue();
        context.Error.Should().Be(OpenIddictConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task AnotherClient_GetsNoPortLeniency()
    {
        var context = await Validate("umbraco-back-office-some-other-integration", "http://127.0.0.1:61566/");

        context.IsRejected.Should().BeTrue();
    }

    [Fact]
    public async Task WithNoRedirectUri_OpenIddictsOwnRuleApplies()
    {
        var context = await Validate(McpClientId, redirectUri: null);

        context.IsRejected.Should().BeTrue("the MCP client has more than one callback, so OpenIddict cannot pick one");
    }

    private static async Task<ValidateAuthorizationRequestContext> Validate(string clientId, string? redirectUri)
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(application);
        manager.Setup(m => m.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(Registered.ToImmutableArray());
        manager.Setup(m => m.ValidateRedirectUriAsync(application, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object _, string uri, CancellationToken _) => Registered.Contains(uri));

        var handler = new WayfinderMcpLoopbackRedirectUri(manager.Object, Options.Create(new WayfinderMcpOptions { ClientId = McpClientId }));
        var context = new ValidateAuthorizationRequestContext(new OpenIddictServerTransaction
        {
            Logger = NullLogger.Instance,
            Request = new OpenIddictRequest { ClientId = clientId, RedirectUri = redirectUri },
        });

        await handler.HandleAsync(context);
        return context;
    }
}
