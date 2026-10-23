using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Controllers;

/// <summary>
/// What the Blueprints screen's "Connect a design partner" panel shows: this site's MCP endpoint,
/// OAuth client and accepted sign-in callbacks. 204 when the host hasn't mapped the MCP sign-in
/// discovery (<see cref="WayfinderMcpDiscoveryEndpoints.MapWayfinderUmbracoMcpOAuthDiscovery"/>),
/// so the panel can say MCP sign-in isn't set up here.
/// </summary>
[Authorize(Policy = WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin)]
[VersionedApiBackOfficeRoute("wayfinder")]
[ApiExplorerSettings(GroupName = "Wayfinder")]
[MapToApi("Wayfinder")]
public class DesignPartnerController(
    EndpointDataSource endpoints,
    IOptions<WayfinderMcpOptions> options,
    IHostEnvironment environment)
    : ManagementApiControllerBase
{
    [HttpGet("design-partner")]
    [ProducesResponseType<DesignPartnerConnection>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Get()
    {
        var resource = endpoints.Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<WayfinderMcpResource>())
            .FirstOrDefault(metadata => metadata is not null);
        if (resource is null)
        {
            return NoContent();
        }

        var siteRoot = new Uri($"{Request.Scheme}://{Request.Host.Value}");
        return Ok(DesignPartnerConnection.Describe(
            siteRoot,
            resource.Path,
            options.Value,
            WayfinderMcpRedirectUris.For(options.Value, environment),
            environment.IsDevelopment()));
    }
}
