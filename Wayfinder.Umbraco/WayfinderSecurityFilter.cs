using Umbraco.Cms.Api.Management.OpenApi;

namespace Wayfinder.Umbraco;

/// <summary>
/// Security filter for the Wayfinder Management API.
/// </summary>
public class WayfinderSecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
    protected override string ApiName => "Wayfinder";
}
