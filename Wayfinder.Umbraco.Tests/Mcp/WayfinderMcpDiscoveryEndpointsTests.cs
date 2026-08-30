using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Tests.Mcp;

/// <summary>
/// The behaviour an MCP client depends on: from the <c>resource_metadata</c> hint in a 401, it
/// fetches the Protected Resource Metadata, reads an authorization server out of it, and fetches
/// that server's metadata to learn where to send the user to log in. These tests walk that chain
/// over real HTTP against the mapped endpoints — not the builders behind them.
/// </summary>
public class WayfinderMcpDiscoveryEndpointsTests : IAsyncLifetime
{
    private const string McpPath = "/wayfinder/service-blueprint-authoring/mcp";
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOptions<WayfinderMcpOptions>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapWayfinderUmbracoMcpOAuthDiscovery(McpPath));
                }))
            .StartAsync();

        _client = _host.GetTestClient();
        _client.BaseAddress = new Uri("https://mcp.council.gov.uk");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<JsonElement> GetJson(string path)
    {
        var response = await _client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "discovery document {0} must be served", path);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task ProtectedResourceMetadata_NamesThisMcpEndpointAndAnAuthorizationServer()
    {
        var doc = await GetJson("/.well-known/oauth-protected-resource");

        doc.GetProperty("resource").GetString()
            .Should().Be("https://mcp.council.gov.uk" + McpPath);
        doc.GetProperty("authorization_servers").EnumerateArray().Single().GetString()
            .Should().Be("https://mcp.council.gov.uk/wayfinder/mcp-auth");
        doc.GetProperty("bearer_methods_supported").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("header");
    }

    [Fact]
    public async Task ProtectedResourceMetadata_IsAlsoServedAtThePathScopedWellKnownLocation()
    {
        (await _client.GetAsync($"/.well-known/oauth-protected-resource{McpPath}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/.well-known/oauth-authorization-server/wayfinder/mcp-auth")]
    [InlineData("/wayfinder/mcp-auth/.well-known/oauth-authorization-server")]
    [InlineData("/wayfinder/mcp-auth/.well-known/openid-configuration")]
    public async Task AuthorizationServerMetadata_PointsTheClientAtUmbracosBackofficeLoginAndTokenEndpoints(string path)
    {
        var doc = await GetJson(path);

        doc.GetProperty("issuer").GetString().Should().Be("https://mcp.council.gov.uk/wayfinder/mcp-auth");
        doc.GetProperty("authorization_endpoint").GetString()
            .Should().Be("https://mcp.council.gov.uk/umbraco/management/api/v1/security/back-office/authorize");
        doc.GetProperty("token_endpoint").GetString()
            .Should().Be("https://mcp.council.gov.uk/umbraco/management/api/v1/security/back-office/token");
        doc.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("S256");
    }

    [Fact]
    public async Task TheAdvertisedAuthorizationServer_ResolvesToRealMetadata()
    {
        // The exact walk an MCP client does: PRM -> authorization_servers[0] -> its metadata.
        var prm = await GetJson("/.well-known/oauth-protected-resource");
        var asUrl = prm.GetProperty("authorization_servers").EnumerateArray().First().GetString()!;

        var authServer = await GetJson(new Uri(asUrl).AbsolutePath + "/.well-known/oauth-authorization-server");

        authServer.GetProperty("issuer").GetString().Should().Be(asUrl);
    }
}
