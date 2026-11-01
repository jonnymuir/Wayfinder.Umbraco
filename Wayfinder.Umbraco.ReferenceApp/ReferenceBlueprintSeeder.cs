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
/// Seeds this reference app's service blueprints (service-blueprints/*.yaml) into
/// Wayfinder.Umbraco's own <see cref="IServiceBlueprintSourceStore"/> on first boot, so the
/// stage/worklist Block Grid blocks placed on the seeded pages (<see cref="ReferenceContentSeeder"/>)
/// have something real to render rather than an empty backoffice:
/// <list type="bullet">
///   <item><c>reference-demo</c> — a minimal two-queue smoke test (citizen submits, caseworker approves).</item>
///   <item><c>njf-coaching-register</c> — the worked example of a configuration-only webhook
///   support system (docs/guides/support-systems.md), resolved by an Umbraco Automate automation.</item>
/// </list>
/// </summary>
public partial class ReferenceBlueprintSeeder(
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

    public const string ContactDefinitionKey = "contact-us";

    private static readonly (string Key, string File)[] Blueprints =
    [
        (DefinitionKey, "reference-demo.yaml"),
        (CoachingRegisterDefinitionKey, "njf-coaching-register.yaml"),
        (ContactDefinitionKey, "contact-us.yaml"),
    ];

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
            var blueprint = ServiceBlueprintYaml.Deserialize(await File.ReadAllTextAsync(path, cancellationToken));

            var result = await store.SaveAsync(blueprint, expectedVersion: 0, cancellationToken);
            if (!result.Saved)
            {
                SaveFailed(logger, key, result);
                continue;
            }

            Seeded(logger, key);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "REFERENCE BLUEPRINT SEEDER: failed to save {DefinitionKey}: {Result}")]
    private static partial void SaveFailed(ILogger logger, string definitionKey, ServiceBlueprintSaveResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "REFERENCE BLUEPRINT SEEDER: seeded {DefinitionKey}.")]
    private static partial void Seeded(ILogger logger, string definitionKey);
}
