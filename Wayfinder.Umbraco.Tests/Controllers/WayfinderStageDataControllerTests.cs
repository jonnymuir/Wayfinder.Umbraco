using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Services;
using Wayfinder.Engine.Stores;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Models.ServiceDesign.BulkData;
using Wayfinder.Services.Sanitization;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Controllers;
using UmbracoProcessManagerEngine = Wayfinder.Umbraco.Services.UmbracoProcessManagerEngine;

namespace Wayfinder.Umbraco.Tests.Controllers;

/// <summary>
/// SECURITY REGRESSION: <see cref="WayfinderStageDataController"/>'s bulk-dataset actions used to
/// call straight into <see cref="IBulkDatasetStore"/> with no caller↔instance ownership check at
/// all — any caller who knew an <c>instanceId</c>/<c>datasetId</c> pair could read, download,
/// correct, or revert another user's ingested data. Every action now calls
/// <see cref="UmbracoProcessManagerEngine.IsOwnedInstance"/> first; these tests exercise that
/// boundary directly against a real in-memory engine and dataset store, not a mock, per this
/// repo's own testing conventions.
/// </summary>
public sealed class WayfinderStageDataControllerTests
{
    private const string DefinitionKey = "stage-data-test";
    private const string TenantId = "tenant-a";
    private const string OwnerUserId = "citizen-owner";
    private const string OtherUserId = "citizen-other";

    private const string BlueprintJson = """
        {
          "definitionKey": "stage-data-test",
          "displayName": "Stage data test",
          "version": 1,
          "initialStage": "start",
          "requestPolicy": "multiple",
          "queues": [ { "key": "citizen", "displayName": "Citizen", "actor": "citizen" } ],
          "stages": [
            {
              "stageKey": "start",
              "displayName": "Start",
              "queueKey": "citizen",
              "components": [ { "type": "panel", "heading": "Start" } ],
              "routes": []
            }
          ]
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class SingleHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set => throw new NotSupportedException(); }
    }

    private static HttpContext HttpContextFor(string userId)
    {
        var context = new DefaultHttpContext();
        context.Items["UserId"] = userId;
        return context;
    }

    private static WayfinderStageDataController BuildController(
        UmbracoProcessManagerEngine engine, IBulkDatasetStore bulkDatasetStore, IServiceRequestFileStorage fileStorage)
    {
        var options = new WayfinderServiceDesignOptions
        {
            ResolveTenantId = _ => TenantId,
            ResolveAccessProfile = _ => new ActorProfile(),
            ResolveUserId = ctx => (string)ctx.Items["UserId"]!,
        };

        return new WayfinderStageDataController(engine, Options.Create(options), fileStorage, bulkDatasetStore);
    }

    /// <summary>Starts a citizen instance owned by <see cref="OwnerUserId"/> and ingests a
    /// one-row bulk dataset against it, returning both ids the controller's routes need.</summary>
    private static async Task<(UmbracoProcessManagerEngine Engine, InMemoryBulkDatasetStore BulkDatasetStore,
        InMemoryServiceRequestFileStorage FileStorage, string InstanceId, string DatasetId)> SeedOwnedInstanceWithDataset()
    {
        var definition = JsonSerializer.Deserialize<ServiceBlueprint>(BlueprintJson, JsonOptions)!;
        var fileStorage = new InMemoryServiceRequestFileStorage();
        var bulkDatasetStore = new InMemoryBulkDatasetStore(fileStorage);
        var engine = new UmbracoProcessManagerEngine(
            NullLogger<UmbracoProcessManagerEngine>.Instance,
            new SingleDefinitionServiceBlueprintStore(definition),
            new PassthroughContentSanitizer(),
            new InMemoryServiceRequestStore(),
            new SingleHttpContextAccessor(new DefaultHttpContext()),
            bulkDatasetStore: bulkDatasetStore);

        var started = engine.GetCurrent(DefinitionKey, TenantId, OwnerUserId, new ActorProfile());

        const string columns = "memberRef,memberName";
        var csv = string.Join('\n', columns, "M-1,Alice");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var storageKey = await fileStorage.SaveAsync(started.InstanceId, "contributionsFile", stream, "contributions.csv");
        var sourceFile = new ServiceRequestFileReference
        {
            StorageKey = storageKey,
            OriginalFileName = "contributions.csv",
            ContentType = "text/csv",
            SizeBytes = csv.Length,
        };

        var ingestResult = await bulkDatasetStore.IngestAsync(
            started.InstanceId, sourceFile,
            [
                new BulkDatasetColumnDescriptor { Key = "memberRef", Title = "Ref", ValueKind = Wayfinder.Models.ServiceDesign.Components.ComponentPropertyValueKind.String, Role = BulkDatasetColumnRole.RowKey },
                new BulkDatasetColumnDescriptor { Key = "memberName", Title = "Name", ValueKind = Wayfinder.Models.ServiceDesign.Components.ComponentPropertyValueKind.String, Role = BulkDatasetColumnRole.Data, Editable = true },
            ]);

        ingestResult.Summary.Should().NotBeNull("the seed CSV must ingest cleanly for these tests to mean anything");
        return (engine, bulkDatasetStore, fileStorage, started.InstanceId, ingestResult.Summary!.DatasetId);
    }

    [Fact]
    public async Task GetSummary_ReturnsNotFound_ForACallerWhoDoesNotOwnTheInstance()
    {
        var (engine, bulkDatasetStore, fileStorage, instanceId, datasetId) = await SeedOwnedInstanceWithDataset();
        var controller = BuildController(engine, bulkDatasetStore, fileStorage);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(OtherUserId) };

        var result = await controller.GetSummary(instanceId, datasetId);

        result.Should().BeOfType<NotFoundResult>(
            "a caller who does not own this instance must never see another citizen's bulk-dataset summary");
    }

    [Fact]
    public async Task GetSummary_ReturnsOk_ForTheOwningCaller()
    {
        var (engine, bulkDatasetStore, fileStorage, instanceId, datasetId) = await SeedOwnedInstanceWithDataset();
        var controller = BuildController(engine, bulkDatasetStore, fileStorage);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(OwnerUserId) };

        var result = await controller.GetSummary(instanceId, datasetId);

        result.Should().BeOfType<OkObjectResult>("the ownership check must not block the instance's own owner");
    }

    [Fact]
    public async Task GetRows_ReturnsNotFound_ForACallerWhoDoesNotOwnTheInstance()
    {
        var (engine, bulkDatasetStore, fileStorage, instanceId, datasetId) = await SeedOwnedInstanceWithDataset();
        var controller = BuildController(engine, bulkDatasetStore, fileStorage);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(OtherUserId) };

        var result = await controller.GetRows(instanceId, datasetId, filter: null, page: null, pageSize: null);

        result.Should().BeOfType<NotFoundResult>(
            "a caller who does not own this instance must never page through another citizen's rows");
    }

    [Fact]
    public async Task CorrectRow_ReturnsNotFound_AndDoesNotMutateTheDataset_ForACallerWhoDoesNotOwnTheInstance()
    {
        var (engine, bulkDatasetStore, fileStorage, instanceId, datasetId) = await SeedOwnedInstanceWithDataset();
        var controller = BuildController(engine, bulkDatasetStore, fileStorage);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(OtherUserId) };

        var result = await controller.CorrectRow(instanceId, datasetId, "M-1", new Dictionary<string, string?> { ["memberName"] = "Mallory" });

        result.Should().BeOfType<NotFoundResult>(
            "a caller who does not own this instance must never rewrite another citizen's row values");

        var rowsAsOwner = await bulkDatasetStore.GetRowsAsync(instanceId, datasetId, BulkDatasetRowFilter.All, 0, 10);
        rowsAsOwner!.Rows.Single(r => r.RowKey == "M-1").CurrentValues["memberName"].Should().Be(
            "Alice", "the rejected non-owner call must never have reached ApplyCorrectionAsync");
    }

    [Fact]
    public async Task RevertCorrections_ReturnsNotFound_ForACallerWhoDoesNotOwnTheInstance()
    {
        var (engine, bulkDatasetStore, fileStorage, instanceId, datasetId) = await SeedOwnedInstanceWithDataset();
        var controller = BuildController(engine, bulkDatasetStore, fileStorage);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(OtherUserId) };

        var result = await controller.RevertCorrections(instanceId, datasetId);

        result.Should().BeOfType<NotFoundResult>(
            "a caller who does not own this instance must never revert another citizen's corrections");
    }

    [Fact]
    public async Task DownloadDataset_ReturnsNotFound_ForACallerWhoDoesNotOwnTheInstance()
    {
        var (engine, bulkDatasetStore, fileStorage, instanceId, datasetId) = await SeedOwnedInstanceWithDataset();
        var controller = BuildController(engine, bulkDatasetStore, fileStorage);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(OtherUserId) };

        var result = await controller.DownloadDataset(instanceId, datasetId);

        result.Should().BeOfType<NotFoundResult>(
            "a caller who does not own this instance must never download another citizen's dataset as CSV");
    }
}
