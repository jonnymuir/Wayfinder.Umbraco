using Microsoft.AspNetCore.Authorization;

namespace Wayfinder.Umbraco;

/// <summary>
/// Authorization marker requirement for <see cref="WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin"/>.
/// </summary>
public class WayfinderAdminRequirement : IAuthorizationRequirement
{
}
