using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Migration that creates the wayfinderServiceRequest table backing the durable,
/// session-scoped <c>IServiceRequestStore</c> implementation for CMS Service Blueprints.
/// </summary>
public class CreateServiceRequestTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists("wayfinderServiceRequest"))
        {
            Create.Table<ServiceRequestSchema>().Do();

            // Sweep query: find every row past its expiry.
            Database.Execute(@"
                CREATE INDEX IX_wayfinderServiceRequest_ExpiresUtc
                ON wayfinderServiceRequest (ExpiresUtc);");

            // FindLatestRequest-style lookup: most recent request for (tenant, user, blueprint).
            Database.Execute(@"
                CREATE INDEX IX_wayfinderServiceRequest_Tenant_User_Blueprint
                ON wayfinderServiceRequest (TenantId, UserId, BlueprintKey);");
        }

        return Task.CompletedTask;
    }
}
