using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Umbraco.Persistence;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// SECURITY REGRESSION (booted host). Anonymous visitors' private links and added browsers live in the
/// database as hashes only, an expired link opens nothing, and the link routes answer the way a real
/// host's middleware order makes them: anonymous, never cached, and an answer posted without the site's
/// antiforgery token is refused.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class VisitorBehaviourTests(ReferenceAppFactory factory)
{
    private IPrivateLinkStore Links => factory.Services.GetRequiredService<IPrivateLinkStore>();

    private IVisitorDeviceStore Devices => factory.Services.GetRequiredService<IVisitorDeviceStore>();

    private T Query<T>(Func<NPoco.IDatabase, T> query)
    {
        using var db = factory.Services.GetRequiredService<IUmbracoDatabaseFactory>().CreateDatabase();
        return query(db);
    }

    [Fact]
    public void AnIssuedLink_OpensItsRequest_AndOnlyItsHashIsStored()
    {
        var instanceId = $"link-probe-{Guid.NewGuid():N}";

        var token = Links.Issue(instanceId, "/contact-us/");

        Links.Redeem(token).Should().Be(new PrivateLink(instanceId, "/contact-us/"));
        var stored = Query(db => db.Fetch<PrivateLinkSchema>("SELECT * FROM wayfinderPrivateLink WHERE InstanceId = @0", instanceId)).Single();
        stored.TokenHash.Should().NotBe(token).And.NotContain(token);
        stored.ExpiresUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void AnExpiredLink_OpensNothing()
    {
        var instanceId = $"link-probe-{Guid.NewGuid():N}";
        var token = Links.Issue(instanceId, "/contact-us/");
        Query(db => db.Execute("UPDATE wayfinderPrivateLink SET ExpiresUtc = @0 WHERE InstanceId = @1", DateTime.UtcNow.AddMinutes(-1), instanceId));

        Links.Redeem(token).Should().BeNull();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public void ALinkCanOnlyEverPointAtThisSite(string pagePath)
    {
        var issue = () => Links.Issue("instance", pagePath);

        issue.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnAddedBrowser_IsStoredByHash_AndCanBeForgotten()
    {
        var hash = $"probe-{Guid.NewGuid():N}";

        Devices.Add(hash, "visitor-1");
        Devices.VisitorIdFor(hash).Should().Be("visitor-1");
        Devices.Remove(hash);

        Devices.VisitorIdFor(hash).Should().BeNull();
    }

    [Fact]
    public async Task FollowingAnUnknownLink_IsAnonymousNeverCachedAndChangesNoIdentity()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/wayfinder/link?t=nobody-issued-this-token-0000000000000000000");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/contact-us/");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeFalse(string.Join("; ", cookies ?? []));
    }

    [Fact]
    public async Task AnAnswerPostedWithoutTheSitesAntiforgeryToken_IsRefused()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsync("/wayfinder/link", new FormUrlEncodedContent(new Dictionary<string, string> { ["decision"] = "continue" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnAnonymousVisitor_GetsTheirOwnHttpOnlySecureCookie()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/contact-us/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("Wayfinder.Visitor=", StringComparison.Ordinal));
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=lax");
    }
}
