using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Security;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco;

/// <summary>
/// Authorizes <see cref="Controllers.ServiceBlueprintAuthoringController"/> requests for backoffice
/// users belonging to one of <see cref="WayfinderServiceDesignOptions.AdminGroupAliases"/>.
/// Blueprints lives under Umbraco's built-in Settings section, so nav visibility is governed
/// entirely by a user group's own <c>AllowedSections</c> — but that's only a navigation
/// convenience: without this handler, any authenticated backoffice user (e.g. a plain Editor
/// with no Settings access) could still call the authoring API directly, bypassing the UI's own
/// access gate entirely.
/// </summary>
public class WayfinderAdminHandler(
    IBackOfficeSecurityAccessor securityAccessor,
    IOptions<WayfinderServiceDesignOptions> options) : AuthorizationHandler<WayfinderAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, WayfinderAdminRequirement requirement)
    {
        var currentUser = securityAccessor.BackOfficeSecurity?.CurrentUser;
        if (currentUser is null)
        {
            return Task.CompletedTask;
        }

        var allowedAliases = options.Value.AdminGroupAliases ?? [];
        if (allowedAliases.Length == 0)
        {
            return Task.CompletedTask;
        }

        var isAdmin = currentUser.Groups?.Any(group =>
            allowedAliases.Contains(group.Alias, StringComparer.OrdinalIgnoreCase)) == true;

        if (isAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
