using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Starter.IntegrationTests;

/// <summary>
/// A stock Umbraco site with only the starter package added is a working Wayfinder demo: a home
/// page listing the services, a sample service the public can use and a caseworker can decide, a
/// service saved later that is live at once, and a design partner endpoint that only a signed-in
/// backoffice user can reach.
/// </summary>
[Collection(BootedStarterSite.Name)]
public sealed class StarterSiteTests(StarterSiteFactory site)
{
    [Fact]
    public async Task TheHomePage_ListsTheSampleService_AndShowsHowToConnectAnAssistant()
    {
        using var browser = site.NewBrowser();

        var html = await browser.GetHtml("/");

        html.Should().Contain("href=\"/services/request-a-callback\"");
        html.Should().Contain("claude mcp add --transport http wayfinder-umbraco https://localhost/wayfinder/service-blueprint-authoring/mcp");
    }

    [Fact]
    public async Task AMemberOfThePublicAsks_TheCaseworkerArrangesIt_AndThePublicSeesTheOutcome()
    {
        using var visitor = site.NewBrowser();
        using var caseworker = site.NewBrowser();
        var name = $"Sam {Guid.NewGuid():N}";

        var form = await visitor.GetHtml("/services/request-a-callback");
        await visitor.Submit(form, "/wayfinder-stage/advance", new Dictionary<string, string>
        {
            ["field:full-name"] = name,
            ["field:phone-number"] = "01234 567890",
            ["field:best-time"] = "Morning",
            ["field:reason"] = "My bin was not collected",
            ["Action"] = "submit",
        });
        (await visitor.GetHtml("/services/request-a-callback")).Should().Contain("The contact centre has your request");

        var signIn = await caseworker.GetHtml("/work-queue");
        (await caseworker.Submit(signIn, "/starter/sign-in")).Should().Be("/work-queue");
        var queue = await caseworker.GetHtml("/work-queue");
        var instanceId = await VisitorsInstanceId(name);
        await caseworker.Submit(queue, $"/request-a-callback/{instanceId}/pickup", new Dictionary<string, string> { ["returnTo"] = "/work-queue" });

        var item = await caseworker.GetHtml($"/work-queue?instanceId={instanceId}&blueprintKey=request-a-callback");
        item.Should().Contain(name);
        await caseworker.Submit(item, "/wayfinder-stage/advance", new Dictionary<string, string> { ["Action"] = "arranged" });

        (await visitor.GetHtml("/services/request-a-callback")).Should().Contain("Your callback is arranged");
    }

    [Fact]
    public async Task AServiceSavedAfterBoot_IsLiveAtItsOwnAddress_AndListedOnTheHomePage()
    {
        var key = $"designed-later-{Guid.NewGuid():N}"[..30];
        var blueprint = ServiceBlueprintYaml.Deserialize($"""
            definitionKey: {key}
            displayName: Report a broken streetlight
            version: 1
            initialStage: where
            queues:
              - key: resident
                displayName: Resident
                actor: resident
            stages:
              - stageKey: where
                displayName: Where is the streetlight?
                queueKey: resident
                components:
                  - type: text
                    fieldKey: location
                    label: Where is the streetlight?
                    required: true
            """);
        var saved = await site.Services.GetRequiredService<IServiceBlueprintSourceStore>().SaveAsync(blueprint, expectedVersion: 0);
        saved.Saved.Should().BeTrue();

        using var visitor = site.NewBrowser();

        (await visitor.GetHtml($"/services/{key}")).Should().Contain("name=\"field:location\"");
        (await visitor.GetHtml("/")).Should().Contain($"href=\"/services/{key}\"");
    }

    [Fact]
    public async Task AServiceThatDoesNotExist_IsNotFound()
    {
        using var visitor = site.NewBrowser();

        var response = await visitor.GetAsync("/services/no-such-service");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheWorkQueue_ShowsNoWorkToSomeoneWhoIsNotSignedInAsTheCaseworker()
    {
        using var visitor = site.NewBrowser();

        var html = await visitor.GetHtml("/work-queue");

        html.Should().Contain("Sign in as Casey Caseworker").And.NotContain("/pickup");
    }

    [Fact]
    public async Task TheDesignPartnerEndpoint_RefusesAnAnonymousCaller_AndPointsItAtSignIn()
    {
        using var anonymous = site.NewBrowser();

        var response = await anonymous.PostAsync(
            "/wayfinder/service-blueprint-authoring/mcp",
            new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource\"");
    }

    [Fact]
    public async Task TheDemoCaseworker_CannotSignInTheDesignPartnerEndpoint()
    {
        using var caseworker = site.NewBrowser();
        await caseworker.Submit(await caseworker.GetHtml("/work-queue"), "/starter/sign-in");

        var response = await caseworker.PostAsync(
            "/wayfinder/service-blueprint-authoring/mcp",
            new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> VisitorsInstanceId(string name)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var match = site.Services.GetRequiredService<IProcessManager>().GetAllInstances()
                .FirstOrDefault(instance => instance.BlueprintKey == "request-a-callback"
                    && instance.FieldValues.Values.Any(value => value?.ToString() == name));
            if (match is not null)
            {
                return match.InstanceId;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No request from {name} reached the engine.");
            }

            await Task.Delay(500);
        }
    }
}
