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
/// Seeds this reference app's service blueprints (service-blueprints/*.json) into
/// Wayfinder.Umbraco's own <see cref="IServiceBlueprintSourceStore"/> on first boot, so the
/// stage/worklist Block Grid blocks placed on the seeded pages (<see cref="ReferenceContentSeeder"/>)
/// have something real to render rather than an empty backoffice:
/// <list type="bullet">
///   <item><c>reference-demo</c> — a minimal two-queue smoke test (citizen submits, caseworker approves).</item>
///   <item><c>njf-coaching-register</c> — the worked example of a configuration-only webhook
///   support system (docs/guides/support-systems.md), resolved by an Umbraco Automate automation.</item>
/// </list>
/// </summary>
public class ReferenceBlueprintSeeder(
    IServiceBlueprintSourceStore store,
    IWebHostEnvironment env,
    IRuntimeState runtimeState,
    ILogger<ReferenceBlueprintSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    /// <summary>The minimal smoke-test blueprint. Kept as a named constant — <see cref="ReferenceContentSeeder"/> binds a block to it.</summary>
    public const string DefinitionKey = "reference-demo";

    /// <summary>The NJF coaching-register blueprint whose registrar review stage calls the <c>njf-coaching-standards</c> support system.</summary>
    public const string CoachingRegisterDefinitionKey = "njf-coaching-register";

    private static readonly (string Key, string File)[] Blueprints =
    [
        (DefinitionKey, "reference-demo.json"),
        (CoachingRegisterDefinitionKey, "njf-coaching-register.json"),
    ];

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

        foreach (var (key, file) in Blueprints)
        {
            if (await store.LoadAsync(key, cancellationToken) is not null)
            {
                continue;
            }

            var path = Path.Combine(env.ContentRootPath, "service-blueprints", file);
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var blueprint = JsonSerializer.Deserialize<ServiceBlueprint>(json, ReadOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize {path}.");

            var result = await store.SaveAsync(blueprint, expectedVersion: 0, cancellationToken);
            if (!result.Saved)
            {
                logger.LogError("REFERENCE BLUEPRINT SEEDER: failed to save {DefinitionKey}: {Result}", key, result);
                continue;
            }

            logger.LogInformation("REFERENCE BLUEPRINT SEEDER: seeded {DefinitionKey}.", key);
        }
    }
}
