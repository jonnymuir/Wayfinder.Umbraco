using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// Seeds the "reference-demo" service blueprint (service-blueprints/reference-demo.json) into
/// Wayfinder.Umbraco's own <see cref="IServiceBlueprintSourceStore"/> on first boot, so the
/// citizen stage block and caseworker worklist block placed on the seeded home page
/// (<see cref="ReferenceContentSeeder"/>) have something real to render — a minimal two-queue
/// blueprint (citizen submits, caseworker approves) rather than an empty backoffice.
/// </summary>
public class ReferenceBlueprintSeeder(
    IServiceBlueprintSourceStore store,
    IWebHostEnvironment env,
    IRuntimeState runtimeState,
    ILogger<ReferenceBlueprintSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string DefinitionKey = "reference-demo";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true
    };

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        if (await store.LoadAsync(DefinitionKey, cancellationToken) is not null)
        {
            return;
        }

        var path = Path.Combine(env.ContentRootPath, "service-blueprints", "reference-demo.json");
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var blueprint = JsonSerializer.Deserialize<ServiceBlueprint>(json, ReadOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {path}.");

        var result = await store.SaveAsync(blueprint, expectedVersion: 0, cancellationToken);
        if (!result.Saved)
        {
            logger.LogError("REFERENCE BLUEPRINT SEEDER: failed to save {DefinitionKey}: {Result}", DefinitionKey, result);
            return;
        }

        logger.LogInformation("REFERENCE BLUEPRINT SEEDER: seeded {DefinitionKey}.", DefinitionKey);
    }
}
