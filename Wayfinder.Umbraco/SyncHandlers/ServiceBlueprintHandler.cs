using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Persistence;
using uSync.BackOffice;
using uSync.BackOffice.Configuration;
using uSync.BackOffice.Services;
using uSync.BackOffice.SyncHandlers;
using uSync.BackOffice.SyncHandlers.Interfaces;
using uSync.BackOffice.SyncHandlers.Models;
using BackOfficeConsts = global::uSync.BackOffice.uSyncConstants;
using ISyncItemFactory = global::uSync.Core.ISyncItemFactory;
using Wayfinder.Umbraco.Persistence;

namespace Wayfinder.Umbraco.SyncHandlers;

/// <summary>
/// uSync handler for backoffice-authored service blueprint definitions — mirrors Prism's own
/// <c>PrismTenantHandler</c> pattern, giving service blueprint definitions the same export/import
/// portability a host's own uSync-portable entities have.
/// </summary>
[SyncHandler("ServiceBlueprintHandler", "Wayfinder Service Blueprints", "ServiceBlueprints",
    BackOfficeConsts.Priorites.USYNC_RESERVED_UPPER + 101,
    Icon = "icon-diagram",
    EntityType = "wayfinderServiceBlueprint")]
public class ServiceBlueprintHandler : SyncHandlerRoot<ServiceBlueprintSchema, ServiceBlueprintSchema>, ISyncHandler
{
    private readonly IUmbracoDatabaseFactory _databaseFactory;

    public override string Group => BackOfficeConsts.Groups.Settings;

    public ServiceBlueprintHandler(
        ILogger<SyncHandlerRoot<ServiceBlueprintSchema, ServiceBlueprintSchema>> logger,
        AppCaches appCaches,
        IShortStringHelper shortStringHelper,
        ISyncFileService syncFileService,
        ISyncEventService mutexService,
        ISyncConfigService uSyncConfig,
        ISyncItemFactory itemFactory,
        IUmbracoDatabaseFactory databaseFactory)
        : base(logger, appCaches, shortStringHelper, syncFileService, mutexService, uSyncConfig, itemFactory)
    {
        _databaseFactory = databaseFactory;
    }

    protected override Task<IEnumerable<ServiceBlueprintSchema>> GetChildItemsAsync(ServiceBlueprintSchema? parent)
    {
        if (parent is not null) return Task.FromResult(Enumerable.Empty<ServiceBlueprintSchema>());
        using var db = _databaseFactory.CreateDatabase();
        return Task.FromResult<IEnumerable<ServiceBlueprintSchema>>(db.Fetch<ServiceBlueprintSchema>());
    }

    protected override Task<IEnumerable<ServiceBlueprintSchema>> GetFoldersAsync(ServiceBlueprintSchema? parent) =>
        Task.FromResult(Enumerable.Empty<ServiceBlueprintSchema>());

    protected override Task<ServiceBlueprintSchema?> GetFromServiceAsync(ServiceBlueprintSchema? item) =>
        Task.FromResult(default(ServiceBlueprintSchema));

    protected override Task<IEnumerable<uSyncAction>> DeleteMissingItemsAsync(
        ServiceBlueprintSchema parent, IEnumerable<Guid> keysToKeep, bool reportOnly) =>
        Task.FromResult(Enumerable.Empty<uSyncAction>());

    protected override string GetItemName(ServiceBlueprintSchema item) => item.DisplayName;
}
