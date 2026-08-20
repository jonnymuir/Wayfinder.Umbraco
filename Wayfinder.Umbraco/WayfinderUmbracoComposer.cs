using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Umbraco.Extensions;

namespace Wayfinder.Umbraco;

/// <summary>
/// Always-on composition for the Wayfinder.Umbraco package: the migration that creates its own
/// tables and Block Grid stage element, the engine/store/stage-rendering infrastructure (see
/// <see cref="Extensions.WayfinderUmbracoServiceCollectionExtensions.AddWayfinderUmbraco"/>), and
/// the backoffice authoring API's Swagger group — all so that a bare package reference gives a
/// working "Blueprints" entry under Umbraco's own Settings section with no host
/// <c>Program.cs</c> wiring at all. Unlike an earlier version of this package (which shipped its
/// own top-level "Wayfinder" section), nothing here needs to grant backoffice section access on
/// startup either — Settings is a built-in section every default install already grants to
/// Administrators, so there's no new section for a host to remember to enable.
/// </summary>
/// <remarks>
/// Calls <c>AddWayfinderUmbraco</c> with a no-op <c>configure</c> — this composer runs
/// automatically with no host-specific input, so it can register everything host-agnostic (the
/// engine, stores, rendering) but genuinely cannot supply
/// <see cref="Configuration.WayfinderServiceDesignOptions.ResolveTenantId"/>/
/// <see cref="Configuration.WayfinderServiceDesignOptions.ResolveAccessProfile"/> itself — only a
/// host knows its own identity model. A host's own explicit call (e.g. Prism's
/// <c>AddPrismCmsServiceBlueprint</c>) supplies those; every registration here is genuinely safe
/// to run twice (TryAdd* throughout, plus an explicit guard on the one AddHostedService call that
/// wasn't TryAdd-safe, and <c>OptionsBuilder.Configure</c> composes rather than overwrites), so
/// calling this unconditionally doesn't double-run anything for a host that also calls it itself
/// — and startup fails fast via <c>ValidateOnStart</c> if no host ever supplies the required
/// resolvers at all, rather than silently working with a null identity.
/// </remarks>
public class WayfinderUmbracoComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, WayfinderMigrationHandler>();

        builder.Services.AddWayfinderUmbraco(_ => { });
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
