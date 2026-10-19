using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Tests.Visitors;

/// <summary>
/// SECURITY REGRESSION (identity). An anonymous visitor is a browser holding a random secret; what their
/// requests record as their owner is a separate id that is no way to act as them. Only a well-formed,
/// known secret makes a browser a visitor, a signed-in person is never one, and signing in takes over
/// what the visitor started only for a person limited to their own requests, never a team member.
/// </summary>
public sealed class VisitorIdentityTests
{
    private const string Cookie = "Wayfinder.Visitor";

    private static readonly ActorProfile Citizen = new() { VisibleQueues = ["visitor"], RestrictToInstanceOwner = true };
    private static readonly ActorProfile Team = new() { VisibleQueues = ["team"], RestrictToInstanceOwner = false };

    [Fact]
    public void ANewVisitor_GetsASecret_AndTheirIdIsNeitherTheSecretNorStoresIt()
    {
        var devices = new InMemoryDevices();
        var context = Visit.WithCookies();

        var userId = Visit.Visitors(devices).ResolveUserId(context);

        var secret = Visit.SetCookie(context, Cookie)!;
        secret.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        userId.Should().StartWith("visitor:").And.NotContain(secret);
        devices.Rows.Should().BeEmpty("nothing is stored for a visitor using one browser");
    }

    [Fact]
    public void TheVisitorCookie_IsHttpOnlySecureAndLax()
    {
        var context = Visit.WithCookies();

        Visit.Visitors(new InMemoryDevices()).ResolveUserId(context);

        var cookie = Visit.SetCookieHeader(context, Cookie)!;
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(Microsoft.Net.Http.Headers.SameSiteMode.Lax);
    }

    [Fact]
    public void AReturningVisitor_IsTheSameVisitor()
    {
        var devices = new InMemoryDevices();
        var first = Visit.WithCookies();
        var userId = Visit.Visitors(devices).ResolveUserId(first);

        var again = Visit.Visitors(devices).ResolveUserId(Visit.WithCookies((Cookie, Visit.SetCookie(first, Cookie)!)));

        again.Should().Be(userId);
    }

    [Fact]
    public void KnowingAVisitorsStoredId_IsNoWayToActAsThem()
    {
        var devices = new InMemoryDevices();
        var victim = Visit.Visitors(devices).ResolveUserId(Visit.WithCookies());
        var storedId = victim["visitor:".Length..];

        var attacker = Visit.Visitors(devices).ResolveUserId(Visit.WithCookies((Cookie, storedId)));

        attacker.Should().NotBe(victim);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("member:42")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // the right shape, but nobody's secret
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/=")]
    public void AForgedOrUnknownCookie_MakesANewVisitor_NeverAnExistingOne(string forged)
    {
        var devices = new InMemoryDevices();
        var existing = Visit.Visitors(devices).ResolveUserId(Visit.WithCookies());

        Visit.Visitors(devices).ResolveUserId(Visit.WithCookies((Cookie, forged))).Should().NotBe(existing);
    }

    [Fact]
    public void ASignedInPerson_IsNeverAVisitor_AndGetsNoVisitorCookie()
    {
        var context = Visit.WithCookies();

        var userId = Visit.Visitors(new InMemoryDevices(), signedIn: _ => "member:7").ResolveUserId(context);

        userId.Should().Be("member:7");
        Visit.SetCookie(context, Cookie).Should().BeNull();
    }

    [Fact]
    public void OneRequest_IsOneVisitor_HoweverOftenItIsAsked()
    {
        var devices = new InMemoryDevices();
        var context = Visit.WithCookies();
        var visitors = Visit.Visitors(devices);

        visitors.ResolveUserId(context).Should().Be(visitors.ResolveUserId(context));
        context.Response.Headers.SetCookie.Should().ContainSingle("one visitor cookie, not one per question");
    }

    [Fact]
    public void ByDefault_OnlyAnUmbracoMemberCountsAsSignedIn()
    {
        var options = new WayfinderVisitorOptions();
        var otherScheme = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], "SomeIdentityProvider")),
        };
        var member = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], IdentityConstants.ApplicationScheme)),
        };

        options.SignedInUserId(otherScheme).Should().BeNull("an identity from any other scheme is never taken for a member");
        options.SignedInUserId(member).Should().Be("member:42");
    }

    [Fact]
    public void SigningIn_MovesWhatTheVisitorStartedOntoTheirAccount_AndForgetsTheBrowserAsTheVisitor()
    {
        var devices = new InMemoryDevices();
        var engine = new Mock<IProcessManager>();
        var first = Visit.WithCookies();
        var visitor = Visit.Visitors(devices).ResolveUserId(first);
        var context = Visit.WithCookies((Cookie, Visit.SetCookie(first, Cookie)!));

        Visit.Visitors(devices, engine, signedIn: _ => "member:7").ClaimForSignedInUser(context, "default", Citizen);

        engine.Verify(e => e.ClaimInstances("default", visitor, "member:7"), Times.Once);
        Visit.SetCookie(context, Cookie).Should().BeEmpty("the visitor cookie is removed");
    }

    [Fact]
    public void ATeamMemberSigningIn_ClaimsNothing_AndKeepsTheVisitorCookie()
    {
        var devices = new InMemoryDevices();
        var engine = new Mock<IProcessManager>();
        var first = Visit.WithCookies();
        Visit.Visitors(devices).ResolveUserId(first);
        var context = Visit.WithCookies((Cookie, Visit.SetCookie(first, Cookie)!));

        Visit.Visitors(devices, engine, signedIn: _ => "member:admin").ClaimForSignedInUser(context, "default", Team);

        engine.Verify(e => e.ClaimInstances(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Visit.SetCookie(context, Cookie).Should().BeNull();
    }

    [Fact]
    public void AnotherBrowser_JoinsTheVisitorWithItsOwnSecret_AndTheFirstKeepsWorking()
    {
        var devices = new InMemoryDevices();
        var phone = Visit.WithCookies();
        var visitor = Visit.Visitors(devices).ResolveUserId(phone);
        var laptop = Visit.WithCookies();

        Visit.Visitors(devices).AddThisBrowser(laptop, visitor).Should().BeTrue();

        var laptopSecret = Visit.SetCookie(laptop, Cookie)!;
        laptopSecret.Should().NotBe(Visit.SetCookie(phone, Cookie));
        Visit.Visitors(devices).ResolveUserId(Visit.WithCookies((Cookie, laptopSecret))).Should().Be(visitor);
        Visit.Visitors(devices).ResolveUserId(Visit.WithCookies((Cookie, Visit.SetCookie(phone, Cookie)!))).Should().Be(visitor);
    }

    [Fact]
    public void ForgettingAnAddedBrowser_StopsItsSecretWorking()
    {
        var devices = new InMemoryDevices();
        var visitor = Visit.Visitors(devices).ResolveUserId(Visit.WithCookies());
        var laptop = Visit.WithCookies();
        Visit.Visitors(devices).AddThisBrowser(laptop, visitor);
        var laptopSecret = Visit.SetCookie(laptop, Cookie)!;

        Visit.Visitors(devices).Forget(Visit.WithCookies((Cookie, laptopSecret)));

        Visit.Visitors(devices).ResolveUserId(Visit.WithCookies((Cookie, laptopSecret))).Should().NotBe(visitor);
    }

    [Fact]
    public void OnlyAVisitorsId_CanBeJoined()
    {
        Visit.Visitors(new InMemoryDevices()).AddThisBrowser(Visit.WithCookies(), "member:7").Should().BeFalse();
    }

    [Fact]
    public void ASecretsOwnerId_IsTheIdThatBrowserIsGiven()
    {
        var context = Visit.WithCookies();
        var userId = Visit.Visitors(new InMemoryDevices()).ResolveUserId(context);

        WayfinderVisitors.UserIdForSecret(Visit.SetCookie(context, Cookie)!).Should().Be(userId);
    }
}
