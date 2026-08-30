using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Wayfinder.Umbraco.Mcp;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Wayfinder.Umbraco.Tests.Mcp;

/// <summary>
/// The observable contract: on startup this handler registers exactly one OpenIddict client with
/// the authorization server, and it must be one an interactive MCP client can complete an
/// OAuth 2.1 (Authorization Code + PKCE, public/no-secret) flow against. Every assertion is made
/// against the descriptor actually handed to <see cref="IOpenIddictApplicationManager"/> —
/// nothing here reaches into the handler's internals.
/// </summary>
public class WayfinderMcpOAuthClientInstallerTests
{
    [Fact]
    public async Task RegistersOneClient_ThatIsPublicPkceAuthorizationCode()
    {
        var client = await RegisterAndCapture(new WayfinderMcpOptions { ClientId = "umbraco-back-office-wayfinder-mcp" });

        client.ClientId.Should().Be("umbraco-back-office-wayfinder-mcp");
        client.ClientType.Should().Be(ClientTypes.Public);
        client.Requirements.Should().Contain(Requirements.Features.ProofKeyForCodeExchange);
        client.Permissions.Should().Contain(new[]
        {
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
        });
    }

    [Fact]
    public async Task InDevelopment_TheClientAcceptsALoopbackCallbackForEveryConfiguredPort()
    {
        var client = await RegisterAndCapture(
            new WayfinderMcpOptions { LocalCallbackPorts = [33418, 40000] },
            environmentName: Environments.Development);

        client.RedirectUris.Select(u => u.ToString()).Should().BeEquivalentTo(new[]
        {
            "http://localhost:33418/callback",
            "http://127.0.0.1:33418/callback",
            "http://localhost:40000/callback",
            "http://127.0.0.1:40000/callback",
        });
    }

    [Fact]
    public async Task OutsideDevelopment_TheClientAcceptsOnlyTheExplicitlyConfiguredHttpsCallbacks()
    {
        var client = await RegisterAndCapture(
            new WayfinderMcpOptions
            {
                LocalCallbackPorts = [33418],
                RedirectUris = ["https://claude.ai/api/mcp/auth_callback"],
            },
            environmentName: Environments.Production);

        client.RedirectUris.Select(u => u.ToString())
            .Should().ContainSingle().Which.Should().Be("https://claude.ai/api/mcp/auth_callback");
        client.RedirectUris.Should().NotContain(u => u.IsLoopback);
    }

    [Fact]
    public async Task AnAlreadyRegisteredClientIsUpdatedInPlace_NotDuplicated()
    {
        var existing = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await Handler(manager, Environments.Development, RuntimeLevel.Run).HandleAsync(
            new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        manager.Verify(m => m.UpdateAsync(existing, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Once);
        manager.Verify(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NothingIsRegisteredBeforeTheRuntimeIsReady()
    {
        var manager = new Mock<IOpenIddictApplicationManager>(MockBehavior.Strict);

        await Handler(manager, Environments.Development, RuntimeLevel.Install).HandleAsync(
            new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        manager.VerifyNoOtherCalls();
    }

    // --- helpers: drive the public handler, observe what it asks the AS to register ---

    private static async Task<OpenIddictApplicationDescriptor> RegisterAndCapture(
        WayfinderMcpOptions options, string environmentName = "Production")
    {
        OpenIddictApplicationDescriptor? captured = null;
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        manager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((descriptor, _) => captured = descriptor);

        await Handler(manager, environmentName, RuntimeLevel.Run, options).HandleAsync(
            new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        manager.Verify(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull("the handler must register the client on first startup");
        return captured!;
    }

    private static WayfinderMcpOAuthClientInstaller Handler(
        Mock<IOpenIddictApplicationManager> manager, string environmentName, RuntimeLevel level,
        WayfinderMcpOptions? options = null) =>
        new(
            manager.Object,
            Options.Create(options ?? new WayfinderMcpOptions()),
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environmentName),
            Mock.Of<IRuntimeState>(r => r.Level == level),
            NullLogger<WayfinderMcpOAuthClientInstaller>.Instance);
}
