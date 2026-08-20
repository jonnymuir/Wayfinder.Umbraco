// Wayfinder.Umbraco's own reference-host orchestrator. Deliberately minimal — a single project,
// no external identity provider or second service to wait on (unlike UmbracoPrism.AppHost's
// Keycloak/MockBusinessApp orchestration): Wayfinder.Umbraco.ReferenceApp is fully self-contained
// (unattended-installs into its own local SQLite file, demo cookie auth, no other dependencies).
// This exists purely so the reference app shows up on the Aspire dashboard with a real, clickable
// URL — see feedback_aspire_launchsettings.md: AddProject<T>() has nothing to expose without the
// project's own Properties/launchSettings.json, which Wayfinder.Umbraco.ReferenceApp already has.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Wayfinder_Umbraco_ReferenceApp>(
    "referenceapp", launchProfileName: "Wayfinder.Umbraco.ReferenceApp");

builder.Build().Run();
