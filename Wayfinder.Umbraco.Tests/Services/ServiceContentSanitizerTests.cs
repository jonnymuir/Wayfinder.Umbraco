using FluentAssertions;
using Wayfinder.Umbraco.Services.Sanitization;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// Behavioural coverage for <see cref="ServiceContentSanitizer"/>, the GDS-aligned allowlist
/// (SEC-003 §4.3) that every rendered component's HTML passes through before <c>@Html.Raw</c>.
/// Allowed markup round-trips; script, event handlers, dangerous URI schemes and off-allowlist
/// elements are stripped. Previously lived in a downstream repo's test project reaching in via
/// <c>InternalsVisibleTo</c>; the type is public now, so its tests live with it.
/// </summary>
public class ServiceContentSanitizerTests
{
    private readonly ServiceContentSanitizer _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void NullOrWhitespace_ReturnsEmpty(string? input)
    {
        _sut.Sanitize(input).Should().BeEmpty();
    }

    [Fact]
    public void AllowlistedBlockAndInlineMarkup_RoundTripsIntact()
    {
        const string html = "<h2>Heading</h2><p>Some <strong>bold</strong> and <em>emphasis</em>.</p><ul><li>one</li><li>two</li></ul>";

        _sut.Sanitize(html).Should().Be(html);
    }

    [Fact]
    public void ScriptTag_IsStripped_LegitimateContentSurvives()
    {
        var result = _sut.Sanitize("<p>Body text</p><script>alert('xss')</script>");

        result.Should().NotContain("<script").And.NotContain("alert(");
        result.Should().Contain("<p>Body text</p>");
    }

    [Fact]
    public void EventHandlerAttributeOnAnAllowlistedTag_IsStripped()
    {
        var result = _sut.Sanitize("<p onclick=\"steal()\">text</p>");

        result.Should().NotContain("onclick").And.NotContain("steal(");
        result.Should().Contain("text");
    }

    [Fact]
    public void JavascriptHrefScheme_IsStripped()
    {
        _sut.Sanitize("<a href=\"javascript:alert(1)\">click</a>").Should().NotContain("javascript:");
    }

    [Fact]
    public void DataTextHtmlHrefScheme_IsStripped()
    {
        _sut.Sanitize("<a href=\"data:text/html,<script>alert(1)</script>\">x</a>").Should().NotContain("data:text/html");
    }

    [Fact]
    public void OffAllowlistElements_AreRemovedWholesale()
    {
        var result = _sut.Sanitize("<img src=\"x\" onerror=\"alert(1)\"><div>d</div><svg onload=\"alert(1)\"><circle/></svg><p>keep</p>");

        result.Should().NotContain("<img").And.NotContain("onerror");
        result.Should().NotContain("<div").And.NotContain("<svg").And.NotContain("onload");
        result.Should().Contain("<p>keep</p>");
    }
}
