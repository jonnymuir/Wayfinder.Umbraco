using System.Net;
using FluentAssertions;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// SECURITY REGRESSION — auth-contract Layer 2: the authorization <em>behaviour</em> of every
/// Wayfinder.Umbraco endpoint, exercised through the real booted reference host (full Umbraco,
/// the demo cookie scheme, the package's controllers wired the way a host wires them).
///
/// Wayfinder.Umbraco.Tests' AuthorizationContractTests (Layer 1) proves each endpoint
/// <em>declares</em> a decision; this proves the pipeline <em>enforces</em> it — [Authorize]
/// actually challenges,
/// the policies actually deny, the antiforgery filter actually fires, and the one endpoint that
/// authenticates imperatively still fails closed. Deny paths only: they need no seeded workflow
/// state and are the security-critical cases. Happy-path 2xx behaviour is covered by the
/// reference app's DAST journey walks and Playwright specs.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class AuthorizationBehaviourTests(ReferenceAppFactory factory)
{
    private HttpClient Anonymous() =>
        factory.CreateClient(new() { AllowAutoRedirect = false });

    private async Task<HttpClient> SignedInAs(string email)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = email });
        var res = await client.PostAsync("/demo/login", form);
        res.StatusCode.Should().Be(HttpStatusCode.Redirect, "the demo login should sign {0} in", email);
        res.Headers.Location!.OriginalString.Should().Be("/", "a recognised demo user lands on the home page, not back at the login");
        return client;
    }

    private const string CitizenEmail = "alex@example.test";
    private const string CaseworkerEmail = "casey@example.test";

    // ---- WayfinderWorklistSurfaceController: [Authorize] (the #78 fix) ----
    // Route shape matches Wayfinder.Engine.Worklist's WorklistRenderer: {ItemUrlPrefix}/
    // {blueprintKey}/{instanceId} (GET, the review redirect) and .../pickup or .../putback
    // (POST, cursorId + a returnTo hidden field/query parameter).

    [Theory]
    [InlineData("/umbraco/wayfinder-worklist/test-blueprint/test-instance/pickup")]
    [InlineData("/umbraco/wayfinder-worklist/test-blueprint/test-instance/putback")]
    public async Task Worklist_pickup_putback_challenge_an_anonymous_caller(string path)
    {
        using var client = Anonymous();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["returnTo"] = "/" });

        var res = await client.PostAsync($"{path}?cursorId=x", form);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "{0} carries [Authorize] — an unauthenticated POST must be challenged before the action runs", path);
        res.Headers.Location!.ToString().Should().Contain("/demo/login",
            "the challenge redirects to the host-configured login path");
    }

    [Fact]
    public async Task Worklist_review_redirect_challenges_an_anonymous_caller()
    {
        using var client = Anonymous();

        var res = await client.GetAsync("/umbraco/wayfinder-worklist/test-blueprint/test-instance?returnTo=/");

        res.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "the review redirect carries [Authorize] too — worklist items are a caseworker/backstage concern");
        res.Headers.Location!.ToString().Should().Contain("/demo/login");
    }

    [Fact]
    public async Task Worklist_pickup_lets_an_authenticated_caller_past_the_authorization_gate()
    {
        using var client = await SignedInAs(CaseworkerEmail);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["returnTo"] = "/" });

        var res = await client.PostAsync("/umbraco/wayfinder-worklist/test-blueprint/no-such-instance/pickup?cursorId=x", form);

        // No antiforgery token → the antiforgery filter rejects with 400. The point: an
        // authenticated caseworker is NOT bounced to /demo/login — [Authorize] passed and the
        // request reached the (antiforgery) filter, exactly as it should for a signed-in caller.
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Worklist_review_redirect_never_forwards_to_an_off_site_returnTo()
    {
        using var client = await SignedInAs(CaseworkerEmail);

        var res = await client.GetAsync(
            "/umbraco/wayfinder-worklist/test-blueprint/test-instance?returnTo=https://evil.example/steal");

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.IsAbsoluteUri.Should().BeFalse("Url.IsLocalUrl rejects an off-site returnTo, falling back to \"/\"");
        res.Headers.Location!.OriginalString.Should().StartWith("/");
    }

    // ---- ServiceBlueprintAuthoringController: [Authorize(Policy = BlueprintsAdmin)] ----

    [Fact]
    public async Task Blueprint_authoring_api_rejects_an_anonymous_caller()
    {
        using var client = Anonymous();
        var res = await client.GetAsync("/umbraco/management/api/v1/wayfinder/service-blueprints/queues");
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Blueprint_authoring_api_rejects_a_front_end_member_cookie()
    {
        using var client = await SignedInAs(CitizenEmail);
        var res = await client.GetAsync("/umbraco/management/api/v1/wayfinder/service-blueprints/queues");
        res.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect },
            "the demo front-end cookie is not the backoffice scheme and does not satisfy BlueprintsAdmin");
    }

    // ---- ServiceRequestPollController: [Authorize(Policy = ServiceRequestPolling)] ----

    [Fact]
    public async Task Poll_endpoint_rejects_an_anonymous_caller()
    {
        using var client = Anonymous();
        var res = await client.GetAsync(
            "/api/wayfinder/workflow/poll?blueprintKey=reference-demo&instanceId=x&knownStateVersion=0");
        res.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect },
            "ServiceRequestPolling requires an authenticated user in the reference host");
    }

    [Fact]
    public async Task Poll_endpoint_admits_an_authenticated_citizen_then_the_engine_scopes_it()
    {
        using var client = await SignedInAs(CitizenEmail);
        var res = await client.GetAsync(
            "/api/wayfinder/workflow/poll?blueprintKey=reference-demo&instanceId=no-such-instance&knownStateVersion=0");
        // Past [Authorize]: any authenticated demo persona may poll. The engine then scopes by
        // ownership, so an unknown instance is a clean 404 — never 401 and never a 5xx.
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- WayfinderStageDataController.DownloadFile: [AllowAnonymous], engine ownership is the boundary ----

    [Fact]
    public async Task File_download_is_not_an_idor__an_unknown_instance_is_404_never_the_file()
    {
        using var client = Anonymous();
        var res = await client.GetAsync(
            $"/umbraco/wayfinder-stage/reference-demo/{Guid.NewGuid()}/files/someField");

        // GetCurrent runs CanAccessInstance and returns ACCESS_DENIED / INSTANCE_NOT_FOUND for a
        // caller who doesn't own the instance, so Render is null and the action returns NotFound().
        // The one thing that must never happen: a 200 with a file body.
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        res.Content.Headers.ContentDisposition.Should().BeNull("no file may be served for an unowned instance");
    }

    // ---- Antiforgery filters actually fire (Layer 1 proves the attribute is present) ----

    [Fact]
    public async Task Stage_advance_rejects_a_post_with_no_antiforgery_token()
    {
        using var client = Anonymous(); // Advance is [AllowAnonymous]; the antiforgery filter still applies
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "y" });

        var res = await client.PostAsync("/umbraco/wayfinder-stage/advance", form);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "[ValidateAntiForgeryToken] must reject a token-less POST — and a 400 (not 401) also confirms " +
            "the endpoint is [AllowAnonymous]: the antiforgery filter ran, not the authorization one");
    }

    // ---- ServiceRequestHubController: imperative fail-closed redirect (KnownImperativeAuth) ----

    [Fact(Skip = "The reference app seeds no serviceRequestHub-typed page, so the route-hijack " +
                 "controller has no URL to hit. Guarded at Layer 1 (KnownImperativeAuth) and manually " +
                 "verified. Enable by seeding a demo hub page in ReferenceContentSeeder.")]
    public Task Hub_redirects_an_anonymous_caller_to_the_configured_login_path() => Task.CompletedTask;

}
