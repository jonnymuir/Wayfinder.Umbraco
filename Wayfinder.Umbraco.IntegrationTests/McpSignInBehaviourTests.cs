using System.Net;
using FluentAssertions;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// SECURITY REGRESSION (booted host). Umbraco's own OAuth server lets the MCP client sign in on any
/// loopback IP port, the way GitHub Copilot CLI does (RFC 8252 section 7.3), and still refuses a
/// callback that differs in anything else.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class McpSignInBehaviourTests(ReferenceAppFactory factory)
{
    [Fact]
    public async Task ASignInThatComesBackToAnEphemeralLoopbackPort_IsAccepted()
    {
        var response = await Authorize("http://127.0.0.1:61566/");

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://127.0.0.1:61566/elsewhere")]
    [InlineData("http://localhost:61566/callback")]
    [InlineData("https://evil.example/callback")]
    public async Task ASignInThatComesBackAnywhereElse_IsRefused(string redirectUri)
    {
        var response = await Authorize(redirectUri);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("redirect_uri");
    }

    private async Task<HttpResponseMessage> Authorize(string redirectUri)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var query = string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = new WayfinderMcpOptions().ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            ["code_challenge_method"] = "S256",
            ["state"] = "probe",
        }.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        return await client.GetAsync($"/umbraco/management/api/v1/security/back-office/authorize?{query}");
    }
}
