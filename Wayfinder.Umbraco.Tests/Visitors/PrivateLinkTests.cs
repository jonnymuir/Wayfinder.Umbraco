using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Stores;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Controllers;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Tests.Visitors;

/// <summary>
/// SECURITY REGRESSION (account takeover and fixation). A private link from an email is the way back to a
/// visitor's request on another device. Following it changes nobody's identity: the request's page asks
/// first, and only "Continue", posted from the site's own page, adds the browser to the visitor. So a link
/// to someone else's request can't silently make your browser theirs. A team member is sent to the team
/// side unchanged, a request that now belongs to a signed-in person opens only for them, a link from
/// another tenant or past its time opens nothing, and the token goes nowhere beyond the link's own response.
/// </summary>
public sealed class PrivateLinkTests
{
    private const string Pending = WayfinderPrivateLinks.PendingCookieName;
    private const string VisitorCookie = "Wayfinder.Visitor";

    private static readonly ActorProfile Citizen = new() { VisibleQueues = ["visitor"], RestrictToInstanceOwner = true };
    private static readonly ActorProfile Team = new() { VisibleQueues = ["team"], RestrictToInstanceOwner = false };

    private sealed class Site
    {
        public InMemoryDevices Devices { get; } = new();
        public InMemoryLinks Links { get; } = new();
        public InMemoryServiceRequestStore Requests { get; } = new();
        public Mock<IProcessManager> Engine { get; } = new();
        public string? SignedIn { get; set; }
        public ActorProfile Profile { get; set; } = Citizen;
        public string Tenant { get; set; } = "default";

        public string VisitorWithRequest(out string instanceId, out string phoneSecret)
        {
            var phone = Visit.WithCookies();
            var visitor = Visitors().ResolveUserId(phone);
            phoneSecret = Visit.SetCookie(phone, VisitorCookie)!;
            instanceId = Guid.NewGuid().ToString();
            Requests.Save(new ServiceRequest { InstanceId = instanceId, BlueprintKey = "contact", TenantId = "default", UserId = visitor });
            return visitor;
        }

        public string LinkTo(string instanceId) => Links.Issue(instanceId, "/contact-us/");

        public WayfinderVisitors Visitors() =>
            new(Options.Create(new WayfinderVisitorOptions { SignedInUserId = _ => SignedIn }), Devices, Engine.Object, new MemoryCache(new MemoryCacheOptions()));

        public WayfinderPrivateLinkController Controller(HttpContext context, bool visitorsEnabled = true)
        {
            var visitorOptions = Options.Create(new WayfinderVisitorOptions { SignedInUserId = _ => SignedIn, StaffLandingPath = "/messages/", ExpiredLinkPath = "/contact-us/?link=expired" });
            var serviceOptions = Options.Create(new WayfinderServiceDesignOptions
            {
                ResolveTenantId = _ => Tenant,
                ResolveAccessProfile = (_, _) => Task.FromResult(Profile),
                LoginPath = "/Account/Login",
            });
            var services = new ServiceCollection();
            if (visitorsEnabled)
            {
                services.AddSingleton(new WayfinderPrivateLinks(Links, Requests, serviceOptions, visitorOptions));
                services.AddSingleton(Visitors());
            }

            return new WayfinderPrivateLinkController(services.BuildServiceProvider(), Engine.Object, serviceOptions, visitorOptions)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };
        }
    }

    private static string Location(IActionResult result) => result.Should().BeOfType<RedirectResult>().Subject.Url;

    [Fact]
    public async Task FollowingALink_ChangesNoIdentity_ItOnlyRemembersTheLinkForThePageToAsk()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out _);
        var token = site.LinkTo(instanceId);
        var laptop = Visit.WithCookies();

        var result = await site.Controller(laptop).Open(token);

        Location(result).Should().Be("/contact-us/");
        Visit.SetCookie(laptop, VisitorCookie).Should().BeNull("no visitor identity is given by following a link");
        Visit.SetCookie(laptop, Pending).Should().Be(token);
        Visit.SetCookieHeader(laptop, Pending)!.HttpOnly.Should().BeTrue();
        site.Devices.Rows.Should().BeEmpty("no browser has been added to the visitor");
    }

    [Fact]
    public async Task Continuing_AddsThisBrowserToTheVisitor_AndOpensTheRequest()
    {
        var site = new Site();
        var visitor = site.VisitorWithRequest(out var instanceId, out _);
        var laptop = Visit.WithCookies((Pending, site.LinkTo(instanceId)));

        var result = await site.Controller(laptop).Answer("continue");

        Location(result).Should().Be($"/contact-us/?instanceId={instanceId}");
        site.Visitors().ResolveUserId(Visit.WithCookies((VisitorCookie, Visit.SetCookie(laptop, VisitorCookie)!))).Should().Be(visitor);
        Visit.SetCookie(laptop, Pending).Should().BeEmpty("the remembered link is cleared");
    }

    [Fact]
    public async Task NotNow_ChangesNothing()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out _);
        var laptop = Visit.WithCookies((Pending, site.LinkTo(instanceId)));

        await site.Controller(laptop).Answer("dismiss");

        Visit.SetCookie(laptop, VisitorCookie).Should().BeNull();
        site.Devices.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAnswerWithoutAFollowedLink_OpensNothing()
    {
        var site = new Site();
        site.VisitorWithRequest(out _, out _);
        var context = Visit.WithCookies();

        Location(await site.Controller(context).Answer("continue")).Should().Be("/contact-us/?link=expired");
        Visit.SetCookie(context, VisitorCookie).Should().BeNull();
    }

    [Theory]
    [InlineData("nobody-issued-this-token-aaaaaaaaaaaaaaaaaa")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnUnknownOrExpiredLink_OpensNothing(string? token)
    {
        var site = new Site();
        site.VisitorWithRequest(out _, out _);
        var context = Visit.WithCookies();

        Location(await site.Controller(context).Open(token)).Should().Be("/contact-us/?link=expired");
        Visit.SetCookie(context, Pending).Should().BeNull();
    }

    [Fact]
    public async Task ALinkToAnotherTenantsRequest_OpensNothing()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out _);
        var token = site.LinkTo(instanceId);
        site.Tenant = "another-tenant";

        Location(await site.Controller(Visit.WithCookies()).Open(token)).Should().Be("/contact-us/?link=expired");
    }

    [Fact]
    public async Task ATeamMember_IsSentToTheTeamSide_AndNothingAboutThemChanges()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out _);
        var token = site.LinkTo(instanceId);
        (site.SignedIn, site.Profile) = ("member:admin", Team);
        var adminBrowser = Visit.WithCookies((Pending, token));

        Location(await site.Controller(Visit.WithCookies()).Open(token)).Should().Be("/messages/");
        Location(await site.Controller(adminBrowser).Answer("continue")).Should().Be("/messages/");

        site.Engine.Verify(e => e.ClaimInstances(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Visit.SetCookie(adminBrowser, VisitorCookie).Should().BeNull();
    }

    [Fact]
    public async Task ARequestThatBelongsToASignedInPerson_OpensOnlyForThem_AndTheTokenGoesNoFurther()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out _);
        site.Requests.TryGet(instanceId, out var request);
        site.Requests.Save(request with { UserId = "member:7", IsAuthenticated = true });
        var token = site.LinkTo(instanceId);

        var someoneElse = await site.Controller(Visit.WithCookies()).Open(token);
        site.SignedIn = "member:7";
        var owner = await site.Controller(Visit.WithCookies()).Open(token);

        Location(someoneElse).Should().StartWith("/Account/Login?ReturnUrl=").And.NotContain(token);
        Location(owner).Should().Be($"/contact-us/?instanceId={instanceId}");
    }

    [Fact]
    public async Task ASignedInPersonContinuing_TakesTheVisitorsRequestsIntoTheirAccount()
    {
        var site = new Site();
        var visitor = site.VisitorWithRequest(out var instanceId, out _);
        site.SignedIn = "member:7";

        await site.Controller(Visit.WithCookies((Pending, site.LinkTo(instanceId)))).Answer("continue");

        site.Engine.Verify(e => e.ClaimInstances("default", visitor, "member:7"), Times.Once);
    }

    [Fact]
    public async Task TheToken_IsNeverCachedReferredOnOrFramed_AndNeverInTheNextAddress()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out _);
        var token = site.LinkTo(instanceId);
        var context = Visit.WithCookies();

        var result = await site.Controller(context).Open(token);

        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        Location(result).Should().NotContain(token);
    }

    [Fact]
    public async Task ABrowserAlreadyTheVisitor_GoesStraightToTheRequest()
    {
        var site = new Site();
        site.VisitorWithRequest(out var instanceId, out var phoneSecret);

        var result = await site.Controller(Visit.WithCookies((VisitorCookie, phoneSecret))).Open(site.LinkTo(instanceId));

        Location(result).Should().Be($"/contact-us/?instanceId={instanceId}");
    }

    [Fact]
    public async Task WithoutVisitors_TheLinkRoutesAreNotFound()
    {
        var site = new Site();

        (await site.Controller(Visit.WithCookies(), visitorsEnabled: false).Open("anything")).Should().BeOfType<NotFoundResult>();
        (await site.Controller(Visit.WithCookies(), visitorsEnabled: false).Answer("continue")).Should().BeOfType<NotFoundResult>();
    }
}
