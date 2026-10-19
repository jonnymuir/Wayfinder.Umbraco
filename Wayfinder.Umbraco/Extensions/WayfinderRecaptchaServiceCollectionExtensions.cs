using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Umbraco.Recaptcha;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Extensions;

/// <summary>Bot protection for stage forms with Google reCAPTCHA v3 (<see cref="RecaptchaSubmissionGuard"/>).</summary>
public static class WayfinderRecaptchaServiceCollectionExtensions
{
    public static IServiceCollection AddWayfinderRecaptcha(this IServiceCollection services, Action<WayfinderRecaptchaOptions> configure)
    {
        services.AddOptions<WayfinderRecaptchaOptions>().BindConfiguration("Wayfinder:Recaptcha").Configure(configure);
        services.AddHttpClient(nameof(RecaptchaSubmissionGuard));
        services.AddSingleton<IStageSubmissionGuard, RecaptchaSubmissionGuard>();
        return services;
    }
}
