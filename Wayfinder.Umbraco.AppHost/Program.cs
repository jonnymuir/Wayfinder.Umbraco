// Wayfinder.Umbraco's own reference-host orchestrator. Deliberately minimal — a single project,
// no external identity provider or second service to wait on (unlike UmbracoPrism.AppHost's
// Keycloak/MockBusinessApp orchestration): Wayfinder.Umbraco.ReferenceApp is fully self-contained
// (unattended-installs into its own local SQLite file, demo cookie auth, no other dependencies).
// This exists so the reference app shows up on the Aspire dashboard with a real, clickable URL
// (see feedback_aspire_launchsettings.md) and, now, so the NJF coaching-standards demo has a real
// mailbox to deliver into and real secrets to sign with.
var builder = DistributedApplication.CreateBuilder(args);

// Mailpit — a real SMTP sink with a web UI, so the Umbraco Automate "Send Email" step in the NJF
// coaching-standards automation actually delivers something a person can look at. Its own "web"
// endpoint gets a dashboard link.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "web")
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
