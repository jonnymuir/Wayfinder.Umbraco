using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// Every booted-host test class shares one <see cref="ReferenceAppFactory"/> — Umbraco is
/// expensive to boot and Wayfinder's static SupportSystemRegistry can only be registered once
/// per process, so a second WebApplicationFactory boot in the same process would fail.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BootedReferenceHost : ICollectionFixture<ReferenceAppFactory>
{
    public const string Name = "Booted reference host";
}

/// <summary>
/// Boots the real <see cref="Wayfinder.Umbraco.ReferenceApp"/> host (full Umbraco, the demo
/// cookie scheme, every Wayfinder.Umbraco controller wired the way a host wires them) for the
/// authorization-contract behavioural suite — auth-contract Layer 2.
///
/// Everything Umbraco would otherwise write into the source tree on first boot is redirected to a
/// per-run temp directory: a fresh SQLite database, the models directory, and the
/// <c>Umbraco:CMS:Imaging:HMACSecretKey</c> (supplied here as config so Umbraco never generates
/// and persists one back into appsettings.json). The temp directory is deleted on dispose.
/// <para/>
/// Set <c>WAYFINDER_INTEGRATION_SQLSERVER</c> to a SQL Server connection string (no database
/// named) and the same suite runs against a fresh SQL Server database instead, dropped on
/// dispose. CI runs it both ways, because a column type SQLite accepts can still truncate on SQL
/// Server.
/// </summary>
public sealed class ReferenceAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SqlServerVariable = "WAYFINDER_INTEGRATION_SQLSERVER";

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "wu-authcontract-" + Guid.NewGuid().ToString("N"));

    /// <summary>Where the host writes each email it sends, as an .eml file.</summary>
    public string MailFolder => Path.Combine(_tempRoot, "mail");

    private readonly SqlServerDatabase? _sqlServer =
        Environment.GetEnvironmentVariable(SqlServerVariable) is { Length: > 0 } server ? new SqlServerDatabase(server) : null;

    /// <summary>
    /// Umbraco reaches its "Run" runtime level asynchronously <em>after</em> the server starts
    /// accepting connections (unattended install + package migrations run in the background), so
    /// the first requests can race a not-ready runtime and 500. Block until a known route answers
    /// stably before any test runs.
    /// </summary>
    public async Task InitializeAsync()
    {
        _sqlServer?.Create();

        using var client = CreateClient(new() { AllowAutoRedirect = false });
        var deadline = DateTime.UtcNow.AddMinutes(3);
        var ok = 0;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await client.GetAsync("/umbraco");
                if (res.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Redirect)
                {
                    if (++ok >= 3)
                    {
                        return;
                    }
                }
                else
                {
                    ok = 0;
                }
            }
            catch (HttpRequestException)
            {
                ok = 0;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException("Umbraco reference host did not reach a ready state within 3 minutes.");
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "umbraco", "Data"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "umbraco", "models"));

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:umbracoDbDSN"] = _sqlServer?.ConnectionString
                    ?? $"Data Source={Path.Combine(_tempRoot, "umbraco", "Data", "Umbraco.sqlite.db")};Cache=Shared;Foreign Keys=True;Pooling=True",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = _sqlServer is null ? "Microsoft.Data.Sqlite" : "Microsoft.Data.SqlClient",

                // Supplied, so Umbraco does not generate one and write it back into the source
                // appsettings.json (the "never commit the HMAC key" rule). A fixed base64 blob —
                // Umbraco binds this as a byte[]; the value is irrelevant, this host serves no
                // images under test.
                ["Umbraco:CMS:Imaging:HMACSecretKey"] = "YXV0aC1jb250cmFjdC1sYXllcjItbm90LWEtcmVhbC1zZWNyZXQtMDA=",

                ["Umbraco:CMS:ModelsBuilder:ModelsDirectory"] = Path.Combine(_tempRoot, "umbraco", "models"),
                ["Umbraco:CMS:ModelsBuilder:AcceptUnsafeModelsDirectory"] = "true",

                ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                ["Umbraco:CMS:Unattended:UpgradeUnattended"] = "true",
                ["Umbraco:CMS:Unattended:PackageMigrationsUnattended"] = "true",

                // Emails the host sends land here, not in the source tree.
                ["Umbraco:CMS:Global:Smtp:PickupDirectoryLocation"] = MailFolder,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _sqlServer?.Drop();
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // A SQLite handle may linger a moment after host shutdown; a stale temp dir is
                // harmless and the OS reaps %TEMP% anyway.
            }
        }
    }
}
