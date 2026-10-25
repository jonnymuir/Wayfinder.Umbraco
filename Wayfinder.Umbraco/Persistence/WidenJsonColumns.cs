using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// The blueprint and request JSON columns were declared without a length, which Umbraco creates
/// as <c>NVARCHAR(255)</c> on SQL Server, so any real blueprint or request failed to save there.
/// The schemas now declare <c>NVARCHAR(MAX)</c>; this widens tables an earlier version already
/// created. SQLite stores both as unbounded <c>TEXT</c> and needs nothing.
/// </summary>
public class WidenJsonColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    private const string SqlServerProviderName = "Microsoft.Data.SqlClient";

    protected override Task MigrateAsync()
    {
        if (SqlSyntax.ProviderName == SqlServerProviderName)
        {
            // A fresh install creates the blueprint table with its Yaml column, already NVARCHAR(MAX).
            if (ColumnExists("wayfinderServiceBlueprint", "Json"))
            {
                Database.Execute("ALTER TABLE wayfinderServiceBlueprint ALTER COLUMN Json NVARCHAR(MAX) NOT NULL");
            }

            Database.Execute("ALTER TABLE wayfinderServiceRequest ALTER COLUMN StateJson NVARCHAR(MAX) NOT NULL");
        }

        return Task.CompletedTask;
    }
}
