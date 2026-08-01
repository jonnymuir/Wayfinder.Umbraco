using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Migration that creates the wayfinderServiceBlueprint table backing the backoffice-hosted
/// CMS Service Blueprint editor's definition store.
/// </summary>
public class CreateServiceBlueprintTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists("wayfinderServiceBlueprint"))
        {
            Create.Table<ServiceBlueprintSchema>().Do();
        }

        return Task.CompletedTask;
    }
}
