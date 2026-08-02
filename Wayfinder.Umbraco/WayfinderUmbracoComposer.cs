using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Umbraco.Extensions;

namespace Wayfinder.Umbraco;

/// <summary>
/// Always-on composition for the Wayfinder.Umbraco package: the migration that creates its own
/// tables, the engine/store/generic stage-rendering infrastructure (see
/// <see cref="Extensions.WayfinderUmbracoServiceCollectionExtensions.AddWayfinderUmbraco"/>), and
/// the backoffice authoring API's Swagger group — all so that a bare package reference gives a
/// working "Wayfinder" backoffice section with no host <c>Program.cs</c> wiring at all, the same
/// composability bar a Prism host already gets for free.
/// </summary>
/// <remarks>
/// <see cref="Extensions.WayfinderUmbracoServiceCollectionExtensions.AddWayfinderUmbraco"/> is
/// also called explicitly by Prism's own composition (<c>AddPrismCmsServiceBlueprint</c>) — every
/// registration inside it is now genuinely safe to run twice (TryAdd* throughout, plus an
/// explicit guard on the one AddHostedService call that wasn't TryAdd-safe), so calling it here
/// unconditionally doesn't double-run anything for a Prism host that also calls it itself.
/// </remarks>
public class WayfinderUmbracoComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, WayfinderMigrationHandler>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, WayfinderSectionAccessSeeder>();

        builder.Services.AddWayfinderUmbraco();
        builder.Services.ConfigureOptions<WayfinderManagementApiConfiguration>();

        // Self-contained, unlike WayfinderUmbracoAuthorizationPolicies.ServiceRequestPolling: a
        // host needs no wiring for this one (see WayfinderAdminHandler's own remarks).
        builder.Services.AddSingleton<IAuthorizationHandler, WayfinderAdminHandler>();
        builder.Services.Configure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new WayfinderAdminRequirement());
            });
        });
    }
}
