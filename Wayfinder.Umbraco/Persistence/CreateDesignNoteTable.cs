using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>Creates the wayfinderDesignNote table backing <see cref="Services.UmbracoDesignNoteStore"/>.</summary>
public class CreateDesignNoteTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists("wayfinderDesignNote"))
        {
            Create.Table<DesignNoteSchema>().Do();
        }

        return Task.CompletedTask;
    }
}
