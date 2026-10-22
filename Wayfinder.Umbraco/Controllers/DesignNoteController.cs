using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Services;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// The blueprint editor's design-note routes, the backoffice twin of the ones
/// <c>MapServiceBlueprintAuthoringApi</c> maps, so the editor's <c>HttpDesignNoteSource</c> works
/// against either. Same <see cref="WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin"/> policy as
/// <see cref="ServiceBlueprintAuthoringController"/>: whoever may edit a blueprint may read and write
/// its notes. A note is attributed to the signed-in backoffice user, never to a name in the request.
/// </summary>
[Authorize(Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin)]
[VersionedApiBackOfficeRoute("wayfinder")]
[ApiExplorerSettings(GroupName = "Wayfinder")]
[MapToApi("Wayfinder")]
public class DesignNoteController(DesignNoteService notes, IBackOfficeSecurityAccessor backOfficeSecurityAccessor)
    : ManagementApiControllerBase
{
    [HttpGet("service-blueprints/{definitionKey}/notes")]
    public async Task<IActionResult> List(string definitionKey, CancellationToken ct) =>
        await notes.ListAsync(definitionKey, ct: ct) is { } list ? Ok(list) : NotFound();

    [HttpPost("service-blueprints/{definitionKey}/notes")]
    public async Task<IActionResult> Add(string definitionKey, [FromBody] AddDesignNoteRequest request, CancellationToken ct) =>
        ToResult(await notes.AddAsync(definitionKey, request.Anchor, request.Text, Author(), ct));

    [HttpPost("service-blueprints/{definitionKey}/notes/{noteId}/replies")]
    public async Task<IActionResult> Reply(string definitionKey, string noteId, [FromBody] DesignNoteTextRequest request, CancellationToken ct) =>
        ToResult(await notes.ReplyAsync(definitionKey, noteId, request.Text, Author(), ct));

    [HttpPost("service-blueprints/{definitionKey}/notes/{noteId}/resolve")]
    public async Task<IActionResult> Resolve(string definitionKey, string noteId, [FromBody] ResolveDesignNoteRequest request, CancellationToken ct) =>
        ToResult(await notes.ResolveAsync(definitionKey, noteId, request.Summary, Author(), request.Version, ct));

    [HttpPost("service-blueprints/{definitionKey}/notes/{noteId}/reopen")]
    public async Task<IActionResult> Reopen(string definitionKey, string noteId, [FromBody] ReopenDesignNoteRequest request, CancellationToken ct) =>
        ToResult(await notes.ReopenAsync(definitionKey, noteId, Author(), request.Reason, ct));

    [HttpDelete("service-blueprints/{definitionKey}/notes/{noteId}")]
    public async Task<IActionResult> Delete(string definitionKey, string noteId, CancellationToken ct) =>
        await notes.DeleteAsync(definitionKey, noteId, ct) ? NoContent() : NotFound();

    private DesignNoteAuthor Author()
    {
        var user = backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        var name = string.IsNullOrWhiteSpace(user?.Name) ? user?.Username : user.Name;
        return new DesignNoteAuthor(name ?? "Backoffice user", DesignNoteAuthorKind.Person);
    }

    private IActionResult ToResult(DesignNoteOutcome outcome) => outcome.Status switch
    {
        DesignNoteOutcomeStatus.Ok => Ok(outcome.Note),
        DesignNoteOutcomeStatus.NotFound => NotFound(outcome),
        DesignNoteOutcomeStatus.Conflict => Conflict(outcome),
        _ => BadRequest(outcome),
    };
}
