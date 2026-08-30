using FluentAssertions;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Tests.Mcp;

public class WayfinderMcpOptionsValidatorTests
{
    private readonly WayfinderMcpOptionsValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        _validator.Validate(null, new WayfinderMcpOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyClientId_Fails(string clientId)
    {
        _validator.Validate(null, new WayfinderMcpOptions { ClientId = clientId })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void NonAbsoluteRedirectUri_Fails()
    {
        _validator.Validate(null, new WayfinderMcpOptions { RedirectUris = ["/callback"] })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void OutOfRangeCallbackPort_Fails()
    {
        _validator.Validate(null, new WayfinderMcpOptions { LocalCallbackPorts = [70000] })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void AbsoluteRedirectUrisAndDocUrl_Pass()
    {
        var result = _validator.Validate(null, new WayfinderMcpOptions
        {
            RedirectUris = ["https://claude.ai/api/mcp/auth_callback"],
            PostLogoutRedirectUris = ["https://claude.ai/api/mcp/auth_callback"],
            ResourceDocumentationUrl = "https://example.test/docs/mcp",
        });

        result.Succeeded.Should().BeTrue();
    }
}
