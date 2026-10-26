using Microsoft.Extensions.Hosting;

namespace Wayfinder.Umbraco.Starter;

/// <summary>Bound from <c>Wayfinder:Starter</c>.</summary>
public sealed class StarterOptions
{
    public const string SectionName = "Wayfinder:Starter";

    /// <summary>
    /// Lets the starter run outside the Development environment, for a shared demo server that
    /// holds nothing real. Anyone who can reach the site can sign in as the caseworker.
    /// </summary>
    public bool AllowOutsideDevelopment { get; set; }

    public static bool MayRun(StarterOptions options, IHostEnvironment environment) =>
        environment.IsDevelopment() || options.AllowOutsideDevelopment;
}
