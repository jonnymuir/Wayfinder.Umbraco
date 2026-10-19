using System.Net;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// The reference app seeds its blueprints and pages on startup, after Umbraco reaches Run (the point the
/// factory waits for), so a slow runner can ask for a page before it exists. Wait for it.
/// </summary>
internal static class SeededPages
{
    public static async Task<(HttpResponseMessage Response, string Html)> GetWhenReady(HttpClient client, string path)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (true)
        {
            var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK || DateTime.UtcNow > deadline)
            {
                return (response, html);
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
