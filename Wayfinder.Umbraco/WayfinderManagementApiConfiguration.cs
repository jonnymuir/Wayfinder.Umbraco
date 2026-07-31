using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Wayfinder.Umbraco;

/// <summary>
/// Configuration for the Wayfinder Management API Swagger documentation.
/// </summary>
public class WayfinderManagementApiConfiguration : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("Wayfinder", new OpenApiInfo
        {
            Title = "Wayfinder Management API",
            Version = "v1"
        });

        options.OperationFilter<WayfinderSecurityFilter>();
    }
}
