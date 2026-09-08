using System.Reflection;
using FluentAssertions;
using Wayfinder.Umbraco.Mcp;

namespace Wayfinder.Umbraco.Tests;

/// <summary>
/// SECURITY REGRESSION: Wayfinder.Umbraco.Mcp plugs a custom IOpenIddictServerHandler into
/// Umbraco's own backoffice OpenIddict server, so Wayfinder.Umbraco.csproj pins
/// OpenIddict.* PrivateAssets="all" purely to compile against — the pin never flows to a host,
/// which restores OpenIddict at *its* Umbraco's version. If $(OpenIddictVersion) is bumped
/// above what Umbraco $(UmbracoVersion) resolves (a Dependabot bump to 7.6.1 once did this),
/// Wayfinder.Umbraco.dll demands an OpenIddict.Server assembly version no real host has on
/// disk — surfacing only as a ReflectionTypeLoadException during Umbraco's TypeFinder scan at
/// boot, invisible to `dotnet build` / `dotnet test` / `dotnet pack`.
///
/// This test reproduces that scan. The test project takes OpenIddict transitively from
/// Umbraco.Cms.Api.Management (via the Wayfinder.Umbraco project reference) — i.e. the version
/// a host actually gets — and forces the CLR to resolve every type in the Wayfinder.Umbraco
/// assembly, exactly as Umbraco.Cms.Core.Composing.TypeFinder does. A version drift throws here.
/// </summary>
public class OpenIddictAssemblyLoadContractTests
{
    [Fact]
    public void EveryTypeInWayfinderUmbracoResolvesAgainstUmbracosOwnOpenIddict()
    {
        // WayfinderMcpDropResourceParameter is the type whose OpenIddict.Server base types force
        // the load; anchor on it so this fails to even compile if that coupling is removed.
        var assembly = typeof(WayfinderMcpDropResourceParameter).Assembly;

        var act = () => assembly.GetTypes();

        act.Should().NotThrow<ReflectionTypeLoadException>(
            "Wayfinder.Umbraco.csproj's OpenIddict pin ($(OpenIddictVersion)) must equal the " +
            "version Umbraco $(UmbracoVersion) resolves — a higher pin loads fine in this " +
            "package's own build but not against any real Umbraco host");
    }
}
