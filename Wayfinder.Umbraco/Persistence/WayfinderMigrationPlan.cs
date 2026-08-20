using Umbraco.Cms.Core.Packaging;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Migration plan for everything the Wayfinder.Umbraco package ships on install — its own
/// database tables, and its Block Grid-composable stage element/data type — independent of any
/// host's own migration plan (e.g. Prism's), since this package is a standalone Umbraco
/// dependency, not something only Prism can install.
/// </summary>
public class WayfinderMigrationPlan : PackageMigrationPlan
{
    public WayfinderMigrationPlan() : base("Wayfinder.Umbraco")
    {
    }

    protected override void DefinePlan()
    {
        To<CreateServiceBlueprintTable>("initial-state")
            .To<CreateServiceRequestTable>("add-cms-service-requests")
            .To<CreateServiceRequestStageBlock>("add-service-request-stage-block");
    }
}
