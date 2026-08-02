using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco;

/// <summary>
/// Grants the "Blueprints" backoffice section (<c>Wayfinder.Section</c>) to every user group
/// listed in <see cref="WayfinderServiceDesignOptions.AdminGroupAliases"/> (the built-in
/// Administrators group by default). Installing an extension manifest never grants a custom
/// section's visibility to any user group automatically — not even to Administrators/superusers,
/// who see only the sections their groups are explicitly allowed — so without this, the section
/// is invisible in the backoffice nav no matter who's logged in, even a correctly-provisioned
/// admin, contradicting the "install the package, get a working authoring UI with zero host
/// wiring" promise this package makes. Runs idempotently — skips a group that already has the
/// section.
/// </summary>
public class WayfinderSectionAccessSeeder(
    IUserGroupService userGroupService,
    IOptions<WayfinderServiceDesignOptions> options,
    IRuntimeState runtimeState,
    ILogger<WayfinderSectionAccessSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string SectionAlias = "Wayfinder.Section";

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run) return;

        foreach (var groupAlias in options.Value.AdminGroupAliases ?? [])
        {
            try
            {
                await EnsureSectionGrantedAsync(groupAlias);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WAYFINDER: Failed to grant Blueprints section to group '{GroupAlias}'; skipping", groupAlias);
            }
        }
    }

    private async Task EnsureSectionGrantedAsync(string groupAlias)
    {
        var group = await userGroupService.GetAsync(groupAlias);
        if (group is null)
        {
            logger.LogDebug("WAYFINDER: User group '{GroupAlias}' not found; skipping section grant", groupAlias);
            return;
        }

        if (group.AllowedSections.Contains(SectionAlias))
        {
            return;
        }

        group.AddAllowedSection(SectionAlias);
        var result = await userGroupService.UpdateAsync(group, Constants.Security.SuperUserKey);

        if (result.Success)
        {
            logger.LogInformation("WAYFINDER: Granted Blueprints section access to user group '{GroupAlias}'", groupAlias);
        }
        else
        {
            logger.LogWarning("WAYFINDER: Failed to save section grant for user group '{GroupAlias}' — {Status}", groupAlias, result.Status);
        }
    }
}
