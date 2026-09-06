using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Extensions;

namespace Wayfinder.Umbraco.Tests.Extensions;

/// <summary>
/// DX REGRESSION: <c>AddWayfinderUmbraco()</c> used to require a host to supply
/// <see cref="WayfinderServiceDesignOptions.ResolveTenantId"/>/<c>ResolveAccessProfile</c> —
/// unbindable <c>Func&lt;&gt;</c> delegates guarded by <c>.ValidateOnStart()</c> — so a clean
/// Umbraco site with only a bare <c>PackageReference</c> threw <c>OptionsValidationException</c>
/// at startup, contradicting the package's own "no host wiring at all" promise. Both now default
/// to safe values (a fixed tenant, a profile that can access no real queue) instead of failing
/// startup.
/// </summary>
public sealed class WayfinderUmbracoServiceCollectionExtensionsTests
{
    private static IOptions<WayfinderServiceDesignOptions> ResolveOptions(Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        register?.Invoke(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<WayfinderServiceDesignOptions>>();
    }

    [Fact]
    public void AddWayfinderUmbraco_ParameterlessOverload_ResolvesOptions_WithoutThrowing()
    {
        var act = () => ResolveOptions(services => services.AddWayfinderUmbraco());

        act.Should().NotThrow<OptionsValidationException>(
            "a bare package reference must boot — the previous required-delegate validation made this throw");
    }

    [Fact]
    public void DefaultResolveTenantId_ReturnsAFixedTenant()
    {
        var options = ResolveOptions(services => services.AddWayfinderUmbraco());

        options.Value.ResolveTenantId.Should().NotBeNull();
        options.Value.ResolveTenantId!(new DefaultHttpContext()).Should().Be("default");
    }

    [Fact]
    public void DefaultResolveAccessProfile_CanAccessNoRealQueue()
    {
        var options = ResolveOptions(services => services.AddWayfinderUmbraco());

        var profile = options.Value.ResolveAccessProfile!(new DefaultHttpContext());

        profile.CanViewQueue("citizen").Should().BeFalse(
            "the zero-config default must deny access to every real queue, not silently allow everything");
        profile.CanStartQueue("citizen").Should().BeFalse();
        profile.CanActInQueue("citizen").Should().BeFalse();
    }

    [Fact]
    public void ConfigureOverload_StillOverridesTheDefaults()
    {
        var options = ResolveOptions(services => services.AddWayfinderUmbraco(o =>
        {
            o.ResolveTenantId = _ => "tenant-a";
        }));

        options.Value.ResolveTenantId!(new DefaultHttpContext()).Should().Be("tenant-a");
    }
}
