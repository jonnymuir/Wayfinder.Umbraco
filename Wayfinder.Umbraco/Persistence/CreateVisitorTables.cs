using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Creates the tables behind anonymous visitors: wayfinderVisitorDevice (<see cref="Visitors.UmbracoVisitorDeviceStore"/>)
/// and wayfinderPrivateLink (<see cref="Visitors.UmbracoPrivateLinkStore"/>).
/// </summary>
public class CreateVisitorTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists("wayfinderVisitorDevice"))
        {
            Create.Table<VisitorDeviceSchema>().Do();
        }

        if (!TableExists("wayfinderPrivateLink"))
        {
            Create.Table<PrivateLinkSchema>().Do();
        }

        return Task.CompletedTask;
    }
}
