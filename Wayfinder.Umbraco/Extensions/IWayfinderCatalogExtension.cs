using Wayfinder.Rendering.GovUk;

namespace Wayfinder.Umbraco.Extensions;

/// <summary>
/// A host's own extension to Wayfinder's component/support-system catalog — implement this on a
/// class in the host's own assembly and it is discovered and run automatically at startup, via
/// Umbraco's own <c>TypeLoader</c> assembly scanning (see <see cref="WayfinderCatalogExtensionHandler"/>).
/// No composer wiring, no ordering, and no forgotten registration call: today, adding a custom
/// component type means a static <c>ComponentTypeRegistry.Register&lt;T&gt;</c> call, a paired
/// <c>GovUkComponentRenderer.RegisterComponent</c>/<c>RegisterField</c> call, and (for a type the
/// REST/MCP authoring surface should accept) a separate <c>AddServiceBlueprintAuthoringApi()</c>
/// registration — three separate calls in the right order, before anything reads the registry,
/// with no compile-time or startup-time check that a host did all three. Implementing this
/// interface instead just needs the class to exist.
/// </summary>
public interface IWayfinderCatalogExtension
{
    /// <summary>
    /// Called once at startup, before any <c>ServiceBlueprint</c> is loaded. Register custom
    /// component/field types via <c>ComponentTypeRegistry.Register&lt;T&gt;</c>, custom support
    /// systems via <c>SupportSystemRegistry.Register</c>, and/or renderer overrides via
    /// <paramref name="renderer"/>'s own <c>RegisterComponent</c>/<c>RegisterField</c> — whichever
    /// this extension needs. All three registries this method can reach are process-wide, static,
    /// and freeze on first read (see their own remarks) — <see cref="WayfinderCatalogExtensionHandler"/>
    /// runs this at <c>UmbracoApplicationStartedNotification</c>, before the site accepts any
    /// request and so before anything could have deserialized/rendered a blueprint yet.
    /// </summary>
    void Register(GovUkComponentRenderer renderer);
}
