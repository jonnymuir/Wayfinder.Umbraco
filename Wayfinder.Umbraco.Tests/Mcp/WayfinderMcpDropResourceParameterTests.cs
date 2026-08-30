using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Wayfinder.Umbraco.Mcp;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Wayfinder.Umbraco.Tests.Mcp;

/// <summary>
/// An MCP client must send the RFC 8707 <c>resource</c> parameter on every authorization and
/// token request; Umbraco's OpenIddict server rejects an unregistered one with
/// <c>invalid_target</c>. This handler strips it — but only for the MCP client, and only when it
/// is actually present.
/// </summary>
public class WayfinderMcpDropResourceParameterTests
{
    private const string McpClientId = "umbraco-back-office-wayfinder-mcp";
    private const string ResourceParam = OpenIddictConstants.Parameters.Resource;

    private readonly WayfinderMcpDropResourceParameter _handler = new(
        Options.Create(new WayfinderMcpOptions { ClientId = McpClientId }));

    private static OpenIddictRequest RequestFrom(string clientId, string? resource)
    {
        var request = new OpenIddictRequest { ClientId = clientId };
        if (resource is not null)
        {
            request.SetParameter(ResourceParam, resource);
        }

        return request;
    }

    [Fact]
    public async Task RemovesTheResourceParameter_FromTheMcpClientsAuthorizationRequest()
    {
        var request = RequestFrom(McpClientId, "https://host/wayfinder/service-blueprint-authoring/mcp");

        await _handler.HandleAsync(new ExtractAuthorizationRequestContext(new OpenIddictServerTransaction()) { Request = request });

        request.HasParameter(ResourceParam).Should().BeFalse();
    }

    [Fact]
    public async Task RemovesTheResourceParameter_FromTheMcpClientsTokenRequest()
    {
        var request = RequestFrom(McpClientId, "https://host/wayfinder/service-blueprint-authoring/mcp");

        await _handler.HandleAsync(new ExtractTokenRequestContext(new OpenIddictServerTransaction()) { Request = request });

        request.HasParameter(ResourceParam).Should().BeFalse();
    }

    [Fact]
    public async Task LeavesAnotherClientsResourceParameterAlone()
    {
        var request = RequestFrom("umbraco-back-office-some-other-integration", "https://host/some/other/api");

        await _handler.HandleAsync(new ExtractAuthorizationRequestContext(new OpenIddictServerTransaction()) { Request = request });

        ((string?)request.GetParameter(ResourceParam)).Should().Be("https://host/some/other/api");
    }

    [Fact]
    public async Task IsANoOpWhenTheMcpClientSendsNoResourceParameter()
    {
        var request = RequestFrom(McpClientId, resource: null);

        await _handler.HandleAsync(new ExtractAuthorizationRequestContext(new OpenIddictServerTransaction()) { Request = request });

        request.HasParameter(ResourceParam).Should().BeFalse();
    }
}
