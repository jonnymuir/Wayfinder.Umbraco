// Wayfinder.Umbraco's own reference-host orchestrator. Deliberately minimal — a single project,
// no external identity provider or second service to wait on (unlike UmbracoPrism.AppHost's
// Keycloak/MockBusinessApp orchestration): Wayfinder.Umbraco.ReferenceApp is fully self-contained
// (unattended-installs into its own local SQLite file, demo cookie auth, no other dependencies).
// This exists so the reference app shows up on the Aspire dashboard with a real, clickable URL
// (see feedback_aspire_launchsettings.md) and, now, so the NJF coaching-standards demo has a real
// mailbox to deliver into and real secrets to sign with.
using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

// Serve Mailpit's web UI over HTTPS with the machine's trusted ASP.NET Core dev certificate, so
// the Aspire dashboard link opens with no warning. Mailpit does not do TLS on its own and has no
// cert of its own, so export the dev cert to PEM once and bind-mount it; `dotnet dev-certs`
// writes the key 0600, and the container runs as a non-root user, so widen it to 0644.
var tlsDir = Path.Combine(Path.GetTempPath(), "wayfinder-mailpit-tls");
Directory.CreateDirectory(tlsDir);
var tlsCert = Path.Combine(tlsDir, "mailpit.pem");
var tlsKey = Path.Combine(tlsDir, "mailpit.key");
ExportDevCertPem(tlsCert, tlsKey);

// Mailpit — a real SMTP sink with a web UI, so the Umbraco Automate "Send Email" step in the NJF
// coaching-standards automation actually delivers something a person can look at.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.31")
    .WithBindMount(tlsDir, "/tls", isReadOnly: true)
    .WithEnvironment("MP_UI_TLS_CERT", "/tls/mailpit.pem")
    .WithEnvironment("MP_UI_TLS_KEY", "/tls/mailpit.key")
    .WithHttpsEndpoint(port: 8025, targetPort: 8025, name: "web")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp");

// Per-run ephemeral secrets — regenerated every launch, never committed. The config-driven
// webhook client signs the outbound invocation with the signing key (HMAC-SHA256); the Automate
// webhook trigger's authenticator validates it with the same key; the callback endpoint and the
// automation's HTTP step share the callback secret. The reference app's AutomateAutomationSeeder
// stamps both into the seeded automation on boot.
var signingKey = Guid.NewGuid().ToString("N");
var callbackSecret = Guid.NewGuid().ToString("N");

builder.AddProject<Projects.Wayfinder_Umbraco_ReferenceApp>(
        "referenceapp", launchProfileName: "Wayfinder.Umbraco.ReferenceApp")
    .WithReference(mailpit.GetEndpoint("smtp"))
    .WaitFor(mailpit)
    // Umbraco's own SMTP client (Automate's Send Email action goes through IEmailSender).
    .WithEnvironment("Umbraco__CMS__Global__Smtp__Host", "localhost")
    .WithEnvironment("Umbraco__CMS__Global__Smtp__Port", "1025")
    .WithEnvironment("Umbraco__CMS__Global__Smtp__From", "njf-coaching-standards@example.test")
    .WithEnvironment("Umbraco__CMS__Global__Smtp__DeliveryMethod", "Network")
    // Upgrade the appsettings default (auth "none", trusted-loopback) to a signed webhook.
    .WithEnvironment("Wayfinder__SupportSystems__0__endpoint__auth__type", "hmac-sha256")
    .WithEnvironment("Wayfinder__SupportSystems__0__endpoint__auth__secretRef", "NJF_STANDARDS_SIGNING_KEY")
    .WithEnvironment("NJF_STANDARDS_SIGNING_KEY", signingKey)
    .WithEnvironment("NJF_STANDARDS_CALLBACK_SECRET", callbackSecret);

builder.Build().Run();

// Exports the trusted ASP.NET Core HTTPS dev certificate as a PEM cert + key pair. `dotnet
// dev-certs https --export-path X.pem --format Pem` writes the cert to X.pem and the key to
// X.key. Reuses an existing export; a stale one (the dev cert rotates yearly) is fixed by
// deleting the temp dir.
static void ExportDevCertPem(string certPath, string keyPath)
{
    if (File.Exists(certPath) && File.Exists(keyPath))
    {
        return;
    }

    var psi = new ProcessStartInfo("dotnet",
        $"dev-certs https --export-path \"{certPath}\" --format Pem --no-password")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    using var proc = Process.Start(psi);
    proc?.WaitForExit(TimeSpan.FromSeconds(30));

    if (!OperatingSystem.IsWindows())
    {
        const UnixFileMode readable = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        if (File.Exists(certPath))
        {
            File.SetUnixFileMode(certPath, readable);
        }

        if (File.Exists(keyPath))
        {
            File.SetUnixFileMode(keyPath, readable);
        }
    }
}
