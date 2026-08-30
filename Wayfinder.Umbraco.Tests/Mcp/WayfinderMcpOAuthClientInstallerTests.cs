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
/// The MCP OAuth client is a real OpenIddict Authorization Code + PKCE public client, registered
/// programmatically at startup (the backoffice UI can't create a public client with redirect
/// URIs). These lock down the descriptor shape and the "create once, update thereafter"
/// convergence — and the rule that a loopback HTTP callback is a Development-only affordance.
/// </summary>
public class WayfinderMcpOAuthClientInstallerTests
{
    private static IHostEnvironment Env(string name) =>
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    [Fact]
    public void BuildDescriptor_IsAPublicPkceAuthorizationCodeClient()
    {
        var descriptor = WayfinderMcpOAuthClientInstaller.BuildDescriptor(
            new WayfinderMcpOptions { ClientId = "umbraco-back-office-wayfinder-mcp" },
            Env(Environments.Production));

        descriptor.ClientId.Should().Be("umbraco-back-office-wayfinder-mcp");
        descriptor.ClientType.Should().Be(ClientTypes.Public);
        descriptor.ConsentType.Should().Be(ConsentTypes.Explicit);
        descriptor.Requirements.Should().Contain(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.Permissions.Should().Contain(new[]
        {
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.Endpoints.Revocation,
            Permissions.Endpoints.EndSession,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
        });
    }

    [Fact]
    public void BuildDescriptor_InDevelopment_AddsLoopbackCallbacksForEveryConfiguredPort()
    {
        var descriptor = WayfinderMcpOAuthClientInstaller.BuildDescriptor(
            new WayfinderMcpOptions { LocalCallbackPorts = [33418, 40000] },
            Env(Environments.Development));

        descriptor.RedirectUris.Select(u => u.ToString()).Should().BeEquivalentTo(new[]
        {
            "http://localhost:33418/callback",
            "http://127.0.0.1:33418/callback",
            "http://localhost:40000/callback",
            "http://127.0.0.1:40000/callback",
        });
    }

    [Fact]
    public void BuildDescriptor_OutsideDevelopment_RegistersNoLoopbackCallback()
    {
        var descriptor = WayfinderMcpOAuthClientInstaller.BuildDescriptor(
            new WayfinderMcpOptions
            {
                LocalCallbackPorts = [33418],
                RedirectUris = ["https://claude.ai/api/mcp/auth_callback"],
                PostLogoutRedirectUris = ["https://claude.ai/api/mcp/auth_callback"],
            },
            Env(Environments.Production));

        descriptor.RedirectUris.Select(u => u.ToString())
            .Should().ContainSingle().Which.Should().Be("https://claude.ai/api/mcp/auth_callback");
        descriptor.RedirectUris.Should().NotContain(u => u.IsLoopback);
        descriptor.PostLogoutRedirectUris.Select(u => u.ToString())
            .Should().ContainSingle().Which.Should().Be("https://claude.ai/api/mcp/auth_callback");
    }

    [Fact]
    public async Task Handle_WhenNoClientExistsYet_CreatesIt()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        await Handler(manager, Environments.Development, RuntimeLevel.Run)
            .HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        manager.Verify(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Once);
        manager.Verify(m => m.UpdateAsync(It.IsAny<object>(), It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenClientAlreadyExists_UpdatesItInPlace()
    {
        var existing = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await Handler(manager, Environments.Development, RuntimeLevel.Run)
            .HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        manager.Verify(m => m.UpdateAsync(existing, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Once);
        manager.Verify(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_BeforeRuntimeIsReady_DoesNothing()
    {
        var manager = new Mock<IOpenIddictApplicationManager>(MockBehavior.Strict);

        await Handler(manager, Environments.Development, RuntimeLevel.Install)
            .HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        manager.VerifyNoOtherCalls();
    }

    private static WayfinderMcpOAuthClientInstaller Handler(
        Mock<IOpenIddictApplicationManager> manager, string environmentName, RuntimeLevel level) =>
        new(
            manager.Object,
            Options.Create(new WayfinderMcpOptions()),
            Env(environmentName),
            Mock.Of<IRuntimeState>(r => r.Level == level),
            NullLogger<WayfinderMcpOAuthClientInstaller>.Instance);
}
