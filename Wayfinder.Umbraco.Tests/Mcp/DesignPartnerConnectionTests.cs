using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Tests.Mcp;

/// <summary>
/// What the "Connect a design partner" panel is told: the MCP server's full address, the OAuth
/// client to sign in with, and, for each MCP client it gives setup for, the sign-in callbacks this
/// site would still have to accept before that client can connect.
/// </summary>
public class DesignPartnerConnectionTests
{
    private static readonly Uri Site = new("https://licensing.example.org");
    private const string McpPath = "/wayfinder/service-blueprint-authoring/mcp";

    [Fact]
    public void Names_the_mcp_server_by_its_full_address_and_the_oauth_client_to_sign_in_with()
    {
        var connection = Describe(new WayfinderMcpOptions { ClientId = "umbraco-back-office-wayfinder-mcp" }, Environments.Production);

        connection.ServerUrl.Should().Be("https://licensing.example.org/wayfinder/service-blueprint-authoring/mcp");
        connection.ClientId.Should().Be("umbraco-back-office-wayfinder-mcp");
        connection.ServerName.Should().Be("wayfinder-umbraco");
    }

    [Fact]
    public void On_a_development_site_claude_code_and_vs_code_are_ready_with_no_configuration()
    {
        var connection = Describe(new WayfinderMcpOptions(), Environments.Development);

        connection.Development.Should().BeTrue();
        connection.CallbackPort.Should().Be(33418);
        Client(connection, "claude-code").Ready.Should().BeTrue();
        Client(connection, "vscode").Ready.Should().BeTrue();
    }

    [Fact]
    public void Claude_desktop_needs_the_claude_connector_callback_until_the_host_adds_it()
    {
        Client(Describe(new WayfinderMcpOptions(), Environments.Development), "claude-desktop")
            .MissingRedirectUris.Should().Equal("https://claude.ai/api/mcp/auth_callback");

        Client(Describe(new WayfinderMcpOptions { RedirectUris = ["https://claude.ai/api/mcp/auth_callback"] }, Environments.Production), "claude-desktop")
            .Ready.Should().BeTrue();
    }

    [Fact]
    public void Outside_development_each_local_client_says_which_callback_the_host_must_add()
    {
        var connection = Describe(new WayfinderMcpOptions(), Environments.Production);

        connection.RedirectUris.Should().BeEmpty();
        Client(connection, "claude-code").MissingRedirectUris.Should().Equal("http://localhost:33418/callback");
        Client(connection, "vscode").MissingRedirectUris.Should().Equal("http://127.0.0.1:33418/");
    }

    [Fact]
    public void Claude_code_is_told_the_callback_port_the_host_registered()
    {
        var connection = Describe(
            new WayfinderMcpOptions { RedirectUris = ["http://localhost:40123/callback"] }, Environments.Production);

        connection.CallbackPort.Should().Be(40123);
        Client(connection, "claude-code").Ready.Should().BeTrue();
    }

    private static DesignPartnerConnection Describe(WayfinderMcpOptions options, string environmentName)
    {
        var environment = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environmentName);
        return DesignPartnerConnection.Describe(
            Site, McpPath, options, WayfinderMcpRedirectUris.For(options, environment), environment.IsDevelopment());
    }

    private static DesignPartnerClientSetup Client(DesignPartnerConnection connection, string client) =>
        connection.Clients.Single(setup => setup.Client == client);
}
