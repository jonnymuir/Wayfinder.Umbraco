using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Extensions;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Services.Sanitization;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;
using Wayfinder.Umbraco.Services.Sanitization;

namespace Wayfinder.Umbraco.Extensions;

/// <summary>
/// Registers Wayfinder's Umbraco-hosted service design building blocks — a DB-backed engine
/// and definition store, the generic stage-rendering infrastructure (nonce, field validation,
/// file upload, content sanitization) shared by every <c>ServiceRequestPageController{T}</c>
/// flow regardless of which <see cref="IBusinessAppProcessManagerClient"/> a host wires up, and
/// uSync portability. Carries no multi-tenancy, auth, or single-queue opinions of its own —
/// see a host's own composition (e.g. Prism's "CMS Workflow" feature) for that layer.
/// </summary>
/// <remarks>
/// Every registration here uses <c>TryAdd*</c> so calling this method more than once (or
/// alongside a host's own registrations for the same interfaces) is safe — a host is free to
/// call this once per composition path that needs it without worrying about double-registration.
/// </remarks>
public static class WayfinderUmbracoServiceCollectionExtensions
{
    public static IServiceCollection AddWayfinderUmbraco(this IServiceCollection services)
    {
        // Boot-time definition loader — deliberately has no dependency on the engine itself;
        // see UmbracoServiceBlueprintBootStore's own remarks for why a combined store would
        // create a DI cycle.
        services.TryAddSingleton<IServiceBlueprintStore, UmbracoServiceBlueprintBootStore>();

        // Durable, session-scoped instance storage.
        services.TryAddSingleton<IServiceRequestStore, UmbracoServiceRequestStore>();

        services.TryAddSingleton<UmbracoProcessManagerEngine>();
        services.TryAddSingleton<IProcessManager>(sp => sp.GetRequiredService<UmbracoProcessManagerEngine>());

        // Authoring-side store — a save reaches the live engine immediately (see
        // UmbracoServiceBlueprintStore's own remarks).
        services.TryAddSingleton<IServiceBlueprintSourceStore, UmbracoServiceBlueprintStore>();

        services.AddServiceBlueprintAuthoring();

        // Wayfinder.Umbraco's own single-queue authoring constraint — see
        // SingleQueueStructuralValidator's remarks for why this belongs here rather than in a
        // host's own composition (unlike Prism's CMS Workflow-specific constraints, this one
        // is a direct consequence of the rendering pipeline this package itself ships).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceBlueprintStructuralValidator, SingleQueueStructuralValidator>());

        // Distributed cache backing the nonce/upload-token services below — works out of the
        // box for single-server dev; a host can replace it with AddStackExchangeRedisCache()
        // or AddDistributedSqlServerCache() for multi-server production.
        services.AddDistributedMemoryCache();

        services.AddOptions<WayfinderServiceDesignOptions>().BindConfiguration("Wayfinder");

        services.TryAddSingleton<IStageNonceService, StageNonceService>();

        // Singleton so ComponentTagHelper's partial-resolution cache (see the resolver's own
        // remarks for why this matters) actually persists across requests instead of being
        // rebuilt from scratch every time.
        services.TryAddSingleton<ComponentPartialResolver>();

        // Wayfinder.Rendering.GovUk's built-in catalog covers most component/field types; this
        // package registers its own richer markup for the handful where its default is a
        // deliberate simplification — see WayfinderUmbracoRenderingOverrides' own remarks.
        // Singleton: the renderer holds only its own override registrations, no per-request
        // state, and registering the overrides is a one-time startup cost.
        services.TryAddSingleton(sp =>
        {
            var renderer = new GovUkComponentRenderer();
            WayfinderUmbracoRenderingOverrides.Register(renderer);
            return renderer;
        });

        // Ganss.Xss-backed GDS allowlist. Registered as singleton: HtmlSanitizer is
        // thread-safe for concurrent Sanitize calls when configuration is not mutated after
        // construction.
        services.TryAddSingleton<IServiceContentSanitizer, ServiceContentSanitizer>();

        // File-upload storage for the "file-upload" component type — disk-backed by default;
        // a host can replace this registration with its own (blob storage, etc.). Fully
        // qualified: Wayfinder.Engine 0.4.1 added its own same-named IServiceRequestFileStorage
        // (Wayfinder.Engine.Abstractions, a different, narrower interface for its own
        // reference-app use) — coincidental collision, not a shared contract; this package's
        // own richer interface (async token/progress-bar upload) is what's registered here.
        services.TryAddSingleton<Services.IServiceRequestFileStorage, DiskServiceRequestFileStorage>();

        // Binds an async-uploaded file to the opaque token the client carries until the
        // stage's real POST — same IDistributedCache mechanism as the nonce service.
        services.TryAddSingleton<IUploadTokenService, UploadTokenService>();

        // AddHostedService isn't itself TryAdd-safe (each call appends another IHostedService
        // registration, and the host starts every one) — guard it explicitly so the "call this
        // more than once is safe" promise above actually holds for every registration in this
        // method, not just the ones that happen to use TryAdd*.
        if (!services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ServiceRequestSweepService)))
        {
            services.AddHostedService<ServiceRequestSweepService>();
        }

        return services;
    }
}
