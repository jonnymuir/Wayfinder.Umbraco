using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Persistence;
using uSync.Core;
using uSync.Core.Models;
using uSync.Core.Serialization;
using Wayfinder.Umbraco.Persistence;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Engine.Abstractions;

namespace Wayfinder.Umbraco.Serialization;

/// <summary>
/// Serializes service blueprint definitions to/from uSync's XML export format — mirrors
/// Prism's own <c>PrismTenantSerializer</c> shape. A successful import also pushes the
/// definition into the live engine, the same promise a backoffice save already makes
/// (see <c>UmbracoServiceBlueprintStore</c>), so an import into a running site takes
/// effect immediately rather than requiring a restart.
/// </summary>
[SyncSerializer("7a2e4f18-9c3b-4d67-a1e5-8f6b2c9d4a71", "Wayfinder Service Blueprint Serializer", "wayfinderServiceBlueprint")]
public class ServiceBlueprintSerializer : SyncSerializerRoot<ServiceBlueprintSchema>, ISyncSerializer<ServiceBlueprintSchema>
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true
    };

    private readonly IUmbracoDatabaseFactory _databaseFactory;
    private readonly IProcessManager _engine;

    public ServiceBlueprintSerializer(
        ILogger<SyncSerializerRoot<ServiceBlueprintSchema>> logger,
        IUmbracoDatabaseFactory databaseFactory,
        IProcessManager engine) : base(logger)
    {
        _databaseFactory = databaseFactory;
        _engine = engine;
    }

    public override Guid ItemKey(ServiceBlueprintSchema item) => DeterministicGuid(item.DefinitionKey);
    public override string ItemAlias(ServiceBlueprintSchema item) => item.DefinitionKey;

    public override Task<ServiceBlueprintSchema?> FindItemAsync(Guid key)
    {
        using var db = _databaseFactory.CreateDatabase();
        var result = db.Fetch<ServiceBlueprintSchema>()
            .FirstOrDefault(w => DeterministicGuid(w.DefinitionKey) == key);
        return Task.FromResult(result);
    }

    public override Task<ServiceBlueprintSchema?> FindItemAsync(string alias)
    {
        using var db = _databaseFactory.CreateDatabase();
        var result = db.Fetch<ServiceBlueprintSchema>()
            .FirstOrDefault(w => w.DefinitionKey == alias);
        return Task.FromResult(result);
    }

    public override Task SaveItemAsync(ServiceBlueprintSchema item)
    {
        using var db = _databaseFactory.CreateDatabase();
        if (item.Id > 0)
            db.Update(item);
        else
            db.Insert(item);

        var blueprint = JsonSerializer.Deserialize<ServiceBlueprint>(item.Json, ReadOptions);
        if (blueprint is not null)
        {
            _engine.UpdateDefinition(item.DefinitionKey, blueprint with { Version = item.Version });
        }

        return Task.CompletedTask;
    }

    public override Task DeleteItemAsync(ServiceBlueprintSchema item)
    {
        using var db = _databaseFactory.CreateDatabase();
        db.Delete(item);
        return Task.CompletedTask;
    }

    protected override Task<SyncAttempt<XElement>> SerializeCoreAsync(ServiceBlueprintSchema item, SyncSerializerOptions options)
    {
        if (item is null)
            return Task.FromResult(SyncAttempt<XElement>.Fail(string.Empty, null, ChangeType.Fail, "Item is null", null));

        var alias = ItemAlias(item);
        var node = InitializeBaseNode(item, alias, 1);
        node.Add(
            new XElement("Info",
                new XElement("DefinitionKey", item.DefinitionKey),
                new XElement("DisplayName", item.DisplayName),
                new XElement("Version", item.Version)),
            new XElement("Definition", new XCData(item.Json)));

        return Task.FromResult(SyncAttempt<XElement>.Succeed(alias, node, ChangeType.Export, new List<uSyncChange>()));
    }

    protected override async Task<SyncAttempt<ServiceBlueprintSchema>> DeserializeCoreAsync(XElement node, SyncSerializerOptions options)
    {
        var existing = await FindItemAsync(node);
        var schema = existing ?? new ServiceBlueprintSchema();

        var info = node.Element("Info");
        schema.DefinitionKey = info?.Element("DefinitionKey")?.Value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(schema.DefinitionKey))
            return SyncAttempt<ServiceBlueprintSchema>.Fail(node.GetAlias(), default, ChangeType.Fail,
                "DefinitionKey is empty — check the exported file", null);

        schema.DisplayName = info?.Element("DisplayName")?.Value ?? string.Empty;
        schema.Version = int.TryParse(info?.Element("Version")?.Value, out var version) ? version : 1;
        schema.Json = node.Element("Definition")?.Value ?? string.Empty;
        schema.UpdatedUtc = DateTime.UtcNow;

        return SyncAttempt<ServiceBlueprintSchema>.Succeed(ItemAlias(schema), schema, ChangeType.Import, new List<uSyncChange>());
    }

    private static Guid DeterministicGuid(string definitionKey)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"wayfinder-service-blueprint:{definitionKey}"));
        return new Guid(hash);
    }
}
