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
/// Registers Wayfinder's Umbraco-hosted service design building blocks — a DB-backed, in-process,
/// authoritative <see cref="Wayfinder.Engine.Abstractions.IProcessManager"/> and definition store,
/// the Block Grid-composable stage rendering (<c>wayfinderServiceRequestStage</c>) and its
/// generic infrastructure (nonce, field validation, file upload, content sanitization), and
/// uSync portability. Carries no multi-tenancy, auth, or single-queue opinions of its own — a
/// host supplies identity resolution via <paramref name="configure"/> below.
/// </summary>
/// <remarks>
/// Every registration here uses <c>TryAdd*</c> so calling this method more than once (or
/// alongside a host's own registrations for the same interfaces) is safe — a host is free to
/// call this once per composition path that needs it without worrying about double-registration.
/// </remarks>
public static class WayfinderUmbracoServiceCollectionExtensions
{
    /// <param name="configure">
    /// Required — must set <see cref="WayfinderServiceDesignOptions.ResolveTenantId"/> and
    /// <see cref="WayfinderServiceDesignOptions.ResolveAccessProfile"/> at minimum (validated at
    /// startup). The engine is authoritative and in-process
    /// (<see cref="Services.UmbracoProcessManagerEngine"/>) — a host resolves identity for it the
    /// same way <c>Wayfinder.Engine.Worklist</c>/<c>Wayfinder.Engine.Journey</c> already ask a
    /// host to, rather than this package assuming a remote "Business App" derives it from a
    /// forwarded bearer token.
    /// </param>
    public static IServiceCollection AddWayfinderUmbraco(
        this IServiceCollection services, Action<WayfinderServiceDesignOptions> configure)
    {
        // Boot-time definition loader — deliberately has no dependency on the engine itself;
        // see UmbracoServiceBlueprintBootStore's own remarks for why a combined store would
        // create a DI cycle.
        services.TryAddSingleton<IServiceBlueprintStore, UmbracoServiceBlueprintBootStore>();

        // Durable, session-scoped instance storage.
        services.TryAddSingleton<IServiceRequestStore, UmbracoServiceRequestStore>();

        services.TryAddSingleton<UmbracoProcessManagerEngine>();
        services.TryAddSingleton<IProcessManager>(sp => sp.GetRequiredService<UmbracoProcessManagerEngine>());

        // Render/advance logic shared by the wayfinderServiceRequestStage Block Grid partial
        // (GET) and WayfinderStageSurfaceController (POST) — stateless beyond its own
        // constructor-injected dependencies, so a singleton is safe.
        services.TryAddSingleton<ServiceRequestStageService>();

        // The caseworker/backstage counterpart — shared by the wayfinderServiceRequestWorklist
        // Block Grid partial and WayfinderWorklistSurfaceController.
        services.TryAddSingleton<ServiceRequestWorklistService>();

        // Authoring-side store — a save reaches the live engine immediately (see
        // UmbracoServiceBlueprintStore's own remarks).
        services.TryAddSingleton<IServiceBlueprintSourceStore, UmbracoServiceBlueprintStore>();

        services.AddServiceBlueprintAuthoring();

        // Distributed cache backing the nonce/upload-token services below — works out of the
        // box for single-server dev; a host can replace it with AddStackExchangeRedisCache()
        // or AddDistributedSqlServerCache() for multi-server production.
        services.AddDistributedMemoryCache();

        services.AddOptions<WayfinderServiceDesignOptions>()
            .BindConfiguration("Wayfinder")
            .Configure(configure)
            .Validate(o => o.ResolveTenantId is not null, $"{nameof(WayfinderServiceDesignOptions.ResolveTenantId)} must be set.")
            .Validate(o => o.ResolveAccessProfile is not null, $"{nameof(WayfinderServiceDesignOptions.ResolveAccessProfile)} must be set.")
            .ValidateOnStart();

        services.TryAddSingleton<IStageNonceService, StageNonceService>();

        // Singleton so ComponentTagHelper's partial-resolution cache (see the resolver's own
        // remarks for why this matters) actually persists across requests instead of being
        // rebuilt from scratch every time.
        services.TryAddSingleton<ComponentPartialResolver>();

        // Wayfinder.Rendering.GovUk's built-in catalog is the gold-standard rendering for every
        // component/field type, slider/stat-group/chart included — this package needs no
        // overrides of its own. Singleton: the renderer holds no per-request state.
        services.TryAddSingleton<GovUkComponentRenderer>();

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

        // The engine's own narrower IServiceRequestFileStorage (see the remark just above) backs
        // IBulkDatasetStore/ISupportSystemClient — EngineServiceRequestFileStorageAdapter delegates
        // to the richer registration above rather than standing up a second, disconnected storage
        // backend, so a citizen-uploaded file and the engine's own dataset ingest see the same
        // bytes. Without this, bulk-dataset-materialize/bulk-dataset-ingest actions silently
        // no-op (ProcessManagerEngine logs "No IBulkDatasetStore registered" and skips) — the
        // bulk-data-review capability (docs/guides/bulk-data-review.md in the core Wayfinder repo)
        // never actually worked in a Wayfinder.Umbraco-hosted blueprint before this.
        services.TryAddSingleton<Wayfinder.Engine.Abstractions.IServiceRequestFileStorage, EngineServiceRequestFileStorageAdapter>();
        services.TryAddSingleton<Wayfinder.Engine.Abstractions.IBulkDatasetStore, Wayfinder.Engine.Stores.InMemoryBulkDatasetStore>();

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
