using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Tests.Mcp;

public class WayfinderMcpChallengeMiddlewareTests
{
    private const string McpPath = "/wayfinder/service-blueprint-authoring/mcp";

    private static async Task<HttpContext> Run(string path, int status, string? wwwAuthenticate)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        context.Request.Path = path;
        context.Response.StatusCode = status;
        if (wwwAuthenticate is not null)
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = wwwAuthenticate;
        }

        var middleware = new WayfinderMcpChallengeMiddleware(_ => Task.CompletedTask, McpPath);
        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task Adds_ResourceMetadataHint_On401FromTheMcpEndpoint()
    {
        var context = await Run(McpPath, StatusCodes.Status401Unauthorized, wwwAuthenticate: null);

        context.Response.Headers[HeaderNames.WWWAuthenticate].ToString()
            .Should().Be("Bearer resource_metadata=\"https://example.test/.well-known/oauth-protected-resource\"");
    }

    [Fact]
    public async Task Appends_ResourceMetadataHint_ToAnExistingBearerChallenge()
    {
        var context = await Run(McpPath, StatusCodes.Status401Unauthorized, "Bearer error=\"invalid_token\"");

        context.Response.Headers[HeaderNames.WWWAuthenticate].ToString()
            .Should().Be("Bearer error=\"invalid_token\", resource_metadata=\"https://example.test/.well-known/oauth-protected-resource\"");
    }

    [Fact]
    public async Task LeavesAlone_AChallengeThatAlreadyCarriesResourceMetadata()
    {
        const string already = "Bearer resource_metadata=\"https://elsewhere.test/x\"";
        var context = await Run(McpPath, StatusCodes.Status401Unauthorized, already);

        context.Response.Headers[HeaderNames.WWWAuthenticate].ToString().Should().Be(already);
    }

    [Fact]
    public async Task LeavesAlone_A401OnADifferentPath()
    {
        var context = await Run("/umbraco/management/api/v1/something", StatusCodes.Status401Unauthorized, null);

        context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate).Should().BeFalse();
    }

    [Fact]
    public async Task LeavesAlone_ASuccessfulMcpResponse()
    {
        var context = await Run(McpPath, StatusCodes.Status200OK, null);

        context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate).Should().BeFalse();
    }
}
