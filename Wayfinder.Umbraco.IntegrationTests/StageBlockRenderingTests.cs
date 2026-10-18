using System.Net;
using FluentAssertions;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// The stage block renders its blueprint's journey through the shared
/// <c>_WayfinderStageForBlueprint</c> view, the same one a host renders from its own block or template.
/// Exercised against the reference app's seeded "Apply" page.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class StageBlockRenderingTests(ReferenceAppFactory factory)
{
    [Fact]
    public async Task TheStageBlock_RendersItsBlueprintsJourneyAsAPostableStageForm()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        // The reference app seeds its blueprints and pages on startup, after Umbraco reaches Run (the
        // point the factory waits for), so a slow runner can get here first. Wait for the page.
        var (status, html) = await GetWhenReady(client, "/apply/");

        status.Should().Be(HttpStatusCode.OK, "the Apply page should render once seeded; last response: {0}", html[..Math.Min(html.Length, 2000)]);
        html.Should().Contain("action=\"/umbraco/wayfinder-stage/advance\"");
        html.Should().Contain("name=\"Nonce\"");
        html.Should().NotContain("No blueprint key configured");
    }

    private static async Task<(HttpStatusCode Status, string Html)> GetWhenReady(HttpClient client, string path)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (true)
        {
            var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK || DateTime.UtcNow > deadline)
            {
                return (response.StatusCode, html);
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
