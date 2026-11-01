using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Umbraco.Persistence;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Serialization;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Boot-time <see cref="IServiceBlueprintStore"/> that seeds <c>UmbracoProcessManagerEngine</c> from the
/// wayfinderServiceBlueprint table at startup. Deliberately has no dependency on
/// <c>IProcessManager</c> — unlike <see cref="UmbracoServiceBlueprintStore"/> (the
/// authoring-side store), which pushes saves back into the live engine and therefore must depend
/// on it. Depending on the engine here would create a DI cycle at construction time.
/// </summary>
public sealed partial class UmbracoServiceBlueprintBootStore(IUmbracoDatabaseFactory databaseFactory)
    : IServiceBlueprintStore
{
    public IReadOnlyDictionary<string, ServiceBlueprint> LoadDefinitions(ILogger logger)
    {
        using var db = databaseFactory.CreateDatabase();
        var rows = db.Fetch<ServiceBlueprintSchema>("SELECT * FROM wayfinderServiceBlueprint");

        var definitions = new Dictionary<string, ServiceBlueprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            try
            {
                definitions[row.DefinitionKey] = ServiceBlueprintYaml.Deserialize(row.Yaml) with { Version = row.Version };
            }
            catch (ServiceBlueprintYamlException ex)
            {
                BlueprintSkipped(logger, ex, row.DefinitionKey);
            }
        }

        DefinitionsLoaded(logger, definitions.Count);
        return definitions;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize CMS Service Blueprint '{Key}' at boot; skipping.")]
    private static partial void BlueprintSkipped(ILogger logger, Exception exception, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "CMS Service Blueprint boot store loaded {Count} definition(s) from the database.")]
    private static partial void DefinitionsLoaded(ILogger logger, int count);
}
