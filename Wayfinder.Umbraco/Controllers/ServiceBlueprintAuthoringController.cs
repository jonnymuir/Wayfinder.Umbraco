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
/// Single-front-stage-queue scope: Wayfinder.Umbraco's rendering pipeline currently only serves
/// one actor's perspective (a visitor-facing page), so only a single queue's worth of content
/// can ever be rendered — <see cref="SingleQueueStructuralValidator"/> enforces that at save
/// time, and <see cref="GetQueues"/> offers exactly one queue to keep the editor's picker
/// consistent with it. Multi-queue/back-stage authoring is future Wayfinder work, not a
/// limitation specific to this controller.
/// <para>
/// <see cref="WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin"/> requires membership of a
/// group listed in <see cref="Configuration.WayfinderServiceDesignOptions.AdminGroupAliases"/> —
/// without it, an authenticated backoffice user who simply lacks Settings-section access could
/// still call this API directly, bypassing nav visibility entirely.
/// </para>
/// </remarks>
[Authorize(Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin)]
[VersionedApiBackOfficeRoute("wayfinder")]
[ApiExplorerSettings(GroupName = "Wayfinder")]
[MapToApi("Wayfinder")]
public class ServiceBlueprintAuthoringController(ServiceBlueprintAuthoringService authoringService) : ManagementApiControllerBase
{
    [HttpGet("service-blueprints/queues")]
    public IActionResult GetQueues() =>
        Ok(new[] { new { queueName = WayfinderFrontStageQueue.Key, displayName = WayfinderFrontStageQueue.DisplayName } });

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
