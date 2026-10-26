using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Wayfinder.Umbraco.Starter.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class BootedStarterSite : ICollectionFixture<StarterSiteFactory>
{
    public const string Name = "Booted starter site";
}

/// <summary>
/// Boots the stock starter site against a fresh SQLite database in a per-run temp directory, the way
/// someone trying Wayfinder would: unattended install, Development environment, nothing else set.
/// </summary>
public sealed class StarterSiteFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "wu-starter-" + Guid.NewGuid().ToString("N"));

    /// <summary>A browser of its own: its own cookies, on https so the visitor cookie is sent back.</summary>
    public HttpClient NewBrowser() =>
        CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    /// <summary>
    /// Umbraco reaches Run, and the starter seeds its content, after the server starts accepting
    /// requests. Wait until the seeded home page answers.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var client = NewBrowser();
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync("/");
                if (response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("Request a callback"))
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException("The starter site did not seed its home page within 3 minutes.");
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "umbraco", "Data"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "umbraco", "models"));

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:umbracoDbDSN"] =
                    $"Data Source={Path.Combine(_tempRoot, "umbraco", "Data", "Umbraco.sqlite.db")};Cache=Shared;Foreign Keys=True;Pooling=True",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",

                // Supplied so Umbraco does not write a generated one back into the site's appsettings.json.
                ["Umbraco:CMS:Imaging:HMACSecretKey"] = "c3RhcnRlci1zaXRlLWludGVncmF0aW9uLW5vdC1hLXJlYWwtc2VjcmV0LTA=",

                ["Umbraco:CMS:ModelsBuilder:ModelsDirectory"] = Path.Combine(_tempRoot, "umbraco", "models"),
                ["Umbraco:CMS:ModelsBuilder:AcceptUnsafeModelsDirectory"] = "true",

                ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Admin",
                ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "admin@example.test",
                ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "Wayfinder123!",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // A SQLite handle can outlive host shutdown for a moment; the OS reaps the temp dir.
            }
        }
    }
}
