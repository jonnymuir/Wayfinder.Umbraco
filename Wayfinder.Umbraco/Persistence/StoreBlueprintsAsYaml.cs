using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Serialization;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Blueprints are stored as YAML: renames <c>wayfinderServiceBlueprint.Json</c> to <c>Yaml</c> and
/// rewrites each stored definition. A JSON definition is already valid YAML, so each one is read
/// and written back as canonical YAML; one that can't be read is logged and left as it was, to
/// fix or reseed, rather than stopping the site from starting.
/// </summary>
public class StoreBlueprintsAsYaml(IMigrationContext context) : AsyncMigrationBase(context)
{
    private const string Table = "wayfinderServiceBlueprint";

    protected override Task MigrateAsync()
    {
        if (!ColumnExists(Table, "Json"))
        {
            return Task.CompletedTask;
        }

        Rename.Column("Json").OnTable(Table).To("Yaml").Do();
        foreach (var row in Database.Fetch<ServiceBlueprintSchema>($"SELECT * FROM {Table}"))
        {
            try
            {
                row.Yaml = ServiceBlueprintYaml.Serialize(ServiceBlueprintYaml.Deserialize(row.Yaml));
                Database.Update(row);
            }
            catch (ServiceBlueprintYamlException ex)
            {
                Logger.LogWarning(ex, "Service blueprint '{DefinitionKey}' could not be converted to YAML and was left as stored.", row.DefinitionKey);
            }
        }

        return Task.CompletedTask;
    }
}
