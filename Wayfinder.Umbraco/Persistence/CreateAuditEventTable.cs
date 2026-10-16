using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Creates the wayfinderAuditEvent table backing <see cref="Services.UmbracoAuditLogStore"/>.
/// </summary>
public class CreateAuditEventTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists("wayfinderAuditEvent"))
        {
            Create.Table<AuditEventSchema>().Do();

            // GetByInstance and the per-instance Query filter.
            Database.Execute(@"
                CREATE INDEX IX_wayfinderAuditEvent_InstanceId
                ON wayfinderAuditEvent (InstanceId, OccurredUtc);");

            // Newest-first queries across instances, and the retention sweep.
            Database.Execute(@"
                CREATE INDEX IX_wayfinderAuditEvent_OccurredUtc
                ON wayfinderAuditEvent (OccurredUtc);");
        }

        return Task.CompletedTask;
    }
}
