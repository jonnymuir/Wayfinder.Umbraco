using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Wayfinder.Engine.Services;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// Backoffice-hosted authoring surface for Wayfinder service blueprint definitions —
/// list/read/validate/save/simulate, backed by the same transport-agnostic
/// <see cref="ServiceBlueprintAuthoringService"/> the AI-authoring toolkit (REST/MCP) and the
/// visual editor's own HTTP source both use elsewhere.
/// A controller, not a mapped minimal-API group, because this is a package (Razor Class
/// Library) with no access to the host application's <c>IEndpointRouteBuilder</c> — Umbraco
/// discovers MVC controllers via assembly scanning instead.
/// </summary>
/// <remarks>
/// <see cref="WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin"/> requires membership of a
/// group listed in <see cref="Configuration.WayfinderServiceDesignOptions.AdminGroupAliases"/> —
/// without it, an authenticated backoffice user who simply lacks Settings-section access could
/// still call this API directly, bypassing nav visibility entirely.
/// </remarks>
[Authorize(Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin)]
[VersionedApiBackOfficeRoute("wayfinder")]
[ApiExplorerSettings(GroupName = "Wayfinder")]
[MapToApi("Wayfinder")]
public class ServiceBlueprintAuthoringController(ServiceBlueprintAuthoringService authoringService) : ManagementApiControllerBase
{
    /// <summary>
    /// Every queue key/display name already declared across every saved blueprint in this
    /// install — an autocomplete aid for the editor's queue picker (see this package's own
    /// <c>wayfinder-service-blueprint-workspace-editor.element.ts</c>, which already falls back
    /// gracefully to an empty list), not a restriction: any queue key an editor types is valid,
    /// this just helps them reuse an existing one (e.g. a shared "caseworker" queue two
    /// blueprints both want) instead of accidentally forking a same-purpose queue under a
    /// second key. No longer limited to a single fixed "front-stage" queue — multi-queue
    /// blueprints (a citizen-facing queue plus a caseworker/backstage one) are fully supported.
    /// </summary>
    [HttpGet("service-blueprints/queues")]
    public async Task<IActionResult> GetQueues(CancellationToken ct)
    {
        var summaries = await authoringService.ListAsync(ct);
        var blueprints = await Task.WhenAll(summaries.Select(s => authoringService.ReadAsync(s.DefinitionKey, ct)));

        var queues = blueprints
            .Where(b => b is not null)
            .SelectMany(b => b!.Queues ?? [])
            .GroupBy(q => q.Key, StringComparer.Ordinal)
            .Select(g => new { queueName = g.Key, displayName = g.First().DisplayName })
            .OrderBy(q => q.queueName, StringComparer.Ordinal)
            .ToArray();

        return Ok(queues);
    }

    [HttpGet("service-blueprints")]
    public async Task<IActionResult> ListServiceBlueprints(CancellationToken ct) =>
        Ok(await authoringService.ListAsync(ct));

    [HttpGet("service-blueprints/{definitionKey}")]
    public async Task<IActionResult> ReadServiceBlueprint(string definitionKey, CancellationToken ct)
    {
        var blueprint = await authoringService.ReadAsync(definitionKey, ct);
        return blueprint is null ? NotFound() : Ok(blueprint);
    }

    [HttpGet("service-blueprints/{definitionKey}/version")]
    public async Task<IActionResult> GetServiceBlueprintVersion(string definitionKey, CancellationToken ct)
    {
        var blueprint = await authoringService.ReadAsync(definitionKey, ct);
        return blueprint is null ? NotFound() : Ok(new { version = blueprint.Version });
    }

    [HttpPost("service-blueprints/validate")]
    public IActionResult ValidateServiceBlueprint([FromBody] ServiceBlueprint blueprint) =>
        Ok(authoringService.Validate(blueprint));

    /// <summary>
    /// The body's own <c>version</c> (already round-tripped by any client that loaded the
    /// blueprint first) IS the expected version for the optimistic-concurrency check — see
    /// <see cref="ServiceBlueprintAuthoringService.SaveAsync"/>.
    /// </summary>
    [HttpPut("service-blueprints/{definitionKey}")]
    public async Task<IActionResult> SaveServiceBlueprint(
        string definitionKey, [FromBody] ServiceBlueprint blueprint, CancellationToken ct)
    {
        if (!string.Equals(blueprint.DefinitionKey, definitionKey, StringComparison.Ordinal))
        {
            return BadRequest(new ServiceBlueprintValidationOutcome(
                false,
                [new ServiceBlueprintDiagnostic(
                    "ROUTE_KEY_MISMATCH",
                    "definitionKey",
                    $"Route key '{definitionKey}' does not match body definitionKey '{blueprint.DefinitionKey}'.")]));
        }

        var outcome = await authoringService.SaveAsync(blueprint, blueprint.Version, ct);
        return outcome.Status switch
        {
            ServiceBlueprintSaveStatus.Saved => Ok(outcome),
            ServiceBlueprintSaveStatus.Conflict => Conflict(outcome),
            _ => BadRequest(outcome)
        };
    }

    [HttpPost("service-blueprints/simulate")]
    public IActionResult SimulateServiceBlueprint([FromBody] ServiceBlueprintSimulationRequest request) =>
        Ok(authoringService.Simulate(request.Blueprint, request.Steps));

    [HttpDelete("service-blueprints/{definitionKey}")]
    public async Task<IActionResult> DeleteServiceBlueprint(string definitionKey, CancellationToken ct)
    {
        var deleted = await authoringService.DeleteAsync(definitionKey, ct);
        return deleted ? Ok() : NotFound();
    }
}

/// <summary>Request body for <see cref="ServiceBlueprintAuthoringController.SimulateServiceBlueprint"/>.</summary>
public sealed record ServiceBlueprintSimulationRequest(
    ServiceBlueprint Blueprint,
    IReadOnlyList<ProcessManagerSimulationStep> Steps);
