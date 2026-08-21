using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// The stage surface's own REST data plane — uploaded-file download and the bulk-data-review
/// component's paging/correcting/reverting endpoints (see docs/guides/bulk-data-review.md in the
/// core Wayfinder repo). A plain MVC controller, not a mapped minimal-API group, for the same
/// reason as <see cref="ServiceBlueprintAuthoringController"/>: this is a Razor Class Library
/// package with no access to the host's <c>IEndpointRouteBuilder</c>.
///
/// Ported from <c>Wayfinder.Engine.Worklist.BulkDatasetReviewExtensions</c>/
/// <c>WorklistExtensions</c>'s own file/bulk-dataset routes, but resolving identity the same way
/// <see cref="Services.ServiceRequestStageService"/> does — via
/// <see cref="WayfinderServiceDesignOptions"/>, not <c>WorklistOptions</c> — since this is the
/// citizen/single-instance stage surface, not the caseworker worklist. <see cref="IProcessManager"/>
/// is still the only real access check: <c>GetCurrent</c> already enforces whatever queue
/// visibility the caller's <c>ActorProfile</c> allows, and <see cref="IBulkDatasetStore"/>
/// independently verifies <c>instanceId</c> owns <c>datasetId</c> regardless (defence in depth) —
/// no extra <c>[Authorize]</c> here would add anything the engine doesn't already check.
///
/// Before this existed, <see cref="Services.ServiceRequestStageService.RenderCurrentAsync"/> never
/// called <c>WithFileDownloadUrls</c>/<c>WithBulkDatasetApiUrls</c> at all — a "bulk-data-review"
/// component rendered its own empty "Nothing to review yet" placeholder on every Wayfinder.Umbraco
/// stage page regardless of how much data was actually ingested, because
/// <see cref="ComponentRenderPayload.DatasetId"/> was always populated but
/// <see cref="ComponentRenderPayload.BulkDatasetApiUrl"/> never was — found live, via a real
/// Playwright walkthrough spec that submitted a genuine file and got back a real
/// summary (1 error/1 warning/3 accepted) but no row cards to act on.
/// </summary>
[Route(RoutePrefix)]
public class WayfinderStageDataController(
    IProcessManager processManager,
    IOptions<WayfinderServiceDesignOptions> optionsAccessor,
    IServiceRequestFileStorage fileStorage,
    IBulkDatasetStore bulkDatasetStore)
    : Microsoft.AspNetCore.Mvc.Controller
{
    public const string RoutePrefix = "/umbraco/wayfinder-stage/{blueprintKey}/{instanceId}";

    /// <summary>Builds the same URL prefixes <see cref="Services.ServiceRequestStageService"/>
    /// hands to <c>WithFileDownloadUrls</c>/<c>WithBulkDatasetApiUrls</c> — kept here, next to the
    /// routes those URLs actually resolve to, so the two can never drift apart.</summary>
    public static (string FilesPrefix, string BulkDatasetsPrefix) BuildUrlPrefixes(string blueprintKey, string instanceId) =>
        ($"/umbraco/wayfinder-stage/{Uri.EscapeDataString(blueprintKey)}/{Uri.EscapeDataString(instanceId)}/files",
         $"/umbraco/wayfinder-stage/{Uri.EscapeDataString(blueprintKey)}/{Uri.EscapeDataString(instanceId)}/bulk-datasets");

    [HttpGet("files/{fieldKey}")]
    public async Task<IActionResult> DownloadFile(string blueprintKey, string instanceId, string fieldKey)
    {
        var options = optionsAccessor.Value;
        var envelope = processManager.GetCurrent(
            blueprintKey, options.ResolveTenantId!(HttpContext), options.ResolveUserId(HttpContext),
            options.ResolveAccessProfile!(HttpContext), instanceId);

        var value = envelope.Render?.Components
            .SelectMany(c => c.Fields)
            .FirstOrDefault(f => f.FieldKey == fieldKey)?.Value;
        var reference = ServiceRequestFileReference.FromFieldValue(value);
        if (reference is null)
        {
            return NotFound();
        }

        var stream = await fileStorage.OpenReadAsync(reference.StorageKey);
        if (stream is null)
        {
            return NotFound();
        }

        var contentType = string.IsNullOrEmpty(reference.ContentType) ? "application/octet-stream" : reference.ContentType;
        return File(stream, contentType, reference.OriginalFileName);
    }

    [HttpGet("bulk-datasets/{datasetId}/summary")]
    public async Task<IActionResult> GetSummary(string instanceId, string datasetId)
    {
        try
        {
            var summary = await bulkDatasetStore.GetSummaryAsync(instanceId, datasetId);
            return summary is null ? NotFound() : Ok(summary);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
    }

    [HttpGet("bulk-datasets/{datasetId}/rows")]
    public async Task<IActionResult> GetRows(string instanceId, string datasetId, string? filter, int? page, int? pageSize)
    {
        var parsedFilter = Enum.TryParse<BulkDatasetRowFilter>(filter, ignoreCase: true, out var f)
            ? f
            : BulkDatasetRowFilter.NeedsAttention;
        var pageIndex = Math.Max(page ?? 0, 0);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);

        try
        {
            var result = await bulkDatasetStore.GetRowsAsync(instanceId, datasetId, parsedFilter, pageIndex, size);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
    }

    [HttpPost("bulk-datasets/{datasetId}/rows/{rowKey}/correct")]
    public async Task<IActionResult> CorrectRow(
        string instanceId, string datasetId, string rowKey,
        [FromBody] Dictionary<string, string?> correctedValues)
    {
        var options = optionsAccessor.Value;
        try
        {
            await bulkDatasetStore.ApplyCorrectionAsync(
                instanceId, datasetId, rowKey, correctedValues, options.ResolveUserId(HttpContext));
            return Content(RenderSyncedActionBar(instanceId, datasetId, options), "text/html");
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("bulk-datasets/{datasetId}/revert")]
    public async Task<IActionResult> RevertCorrections(string instanceId, string datasetId)
    {
        var options = optionsAccessor.Value;
        try
        {
            await bulkDatasetStore.RevertCorrectionsAsync(instanceId, datasetId, options.ResolveUserId(HttpContext));
            return Content(RenderSyncedActionBar(instanceId, datasetId, options), "text/html");
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("bulk-datasets/{datasetId}/download")]
    public async Task<IActionResult> DownloadDataset(string instanceId, string datasetId)
    {
        ServiceRequestFileReference materialized;
        try
        {
            // A pure human-facing export, not tied to any real blueprint field — targetFieldKey
            // here is just IServiceRequestFileStorage's own partition key, never read back by the
            // engine.
            materialized = await bulkDatasetStore.MaterializeAsync(
                instanceId, datasetId, targetFieldKey: "bulkDatasetDownload", fileName: "contributions.csv",
                sanitizeForHumanExport: true);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        var stream = await fileStorage.OpenReadAsync(materialized.StorageKey);
        return stream is null ? NotFound() : File(stream, "text/csv", materialized.OriginalFileName);
    }

    private string RenderSyncedActionBar(string instanceId, string datasetId, WayfinderServiceDesignOptions options)
    {
        var envelope = processManager.SyncBulkDatasetSyncState(
            instanceId, options.ResolveTenantId!(HttpContext), options.ResolveUserId(HttpContext),
            options.ResolveAccessProfile!(HttpContext), datasetId);
        return envelope.Render is { } render
            ? GovUkComponentRenderer.RenderActionButtons(render.AvailableActions, envelope.StateVersion)
            : GovUkComponentRenderer.RenderActionButtons([], envelope.StateVersion);
    }
}
