using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Extensions;

/// <summary>
/// Lets people use a service without signing in, with a way back to their request: an anonymous visitor
/// is a browser cookie (<see cref="WayfinderVisitors"/>), a private link in an email adds another browser
/// (<see cref="WayfinderPrivateLinks"/>), and when they sign in, what they started is moved onto their
/// account. Optional: call it alongside <see cref="WayfinderUmbracoServiceCollectionExtensions.AddWayfinderUmbraco(IServiceCollection, Action{WayfinderServiceDesignOptions})"/>,
/// which still decides what each person may do (<see cref="WayfinderServiceDesignOptions.ResolveAccessProfile"/>).
/// </summary>
public static class WayfinderVisitorServiceCollectionExtensions
{
    /// <summary>
    /// Resolves user ids for Wayfinder: the signed-in person's (<see cref="WayfinderVisitorOptions.SignedInUserId"/>),
    /// otherwise the visitor's. Setting <see cref="WayfinderServiceDesignOptions.ResolveUserId"/> as well is
    /// refused at startup, since one of the two would quietly be ignored.
    /// </summary>
    public static IServiceCollection AddWayfinderVisitors(this IServiceCollection services, Action<WayfinderVisitorOptions>? configure = null)
    {
        services.AddOptions<WayfinderVisitorOptions>()
            .BindConfiguration("Wayfinder:Visitors")
            .Configure(configure ?? (_ => { }))
            .Validate(o => o.PrivateLinkLifetime > TimeSpan.Zero && o.CookieLifetime > TimeSpan.Zero, "Wayfinder:Visitors lifetimes must be positive.")
            .Validate(o => WayfinderVisitors.IsLocalPath(o.StaffLandingPath) && WayfinderVisitors.IsLocalPath(o.ExpiredLinkPath),
                "Wayfinder:Visitors:StaffLandingPath and ExpiredLinkPath must be paths on this site, such as \"/\".")
            .ValidateOnStart();

        services.AddOptions<WayfinderServiceDesignOptions>()
            .PostConfigure(options =>
            {
                if (!options.ResolvesUserIdByDefault)
                {
                    throw new InvalidOperationException(
                        "AddWayfinderVisitors resolves Wayfinder's user ids; don't set WayfinderServiceDesignOptions.ResolveUserId as well. " +
                        "Use WayfinderVisitorOptions.SignedInUserId for how a signed-in person is identified.");
                }

                options.ResolveUserId = context => context.RequestServices.GetRequiredService<WayfinderVisitors>().ResolveUserId(context);
            });

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IVisitorDeviceStore, UmbracoVisitorDeviceStore>();
        services.TryAddSingleton<IPrivateLinkStore, UmbracoPrivateLinkStore>();
        services.TryAddSingleton<WayfinderVisitors>();
        services.TryAddSingleton<WayfinderPrivateLinks>();
        return services;
    }
}
