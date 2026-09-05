using FluentAssertions;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco.Extensions;

namespace Wayfinder.Umbraco.Tests;

/// <summary>
/// DX REGRESSION: extending Wayfinder.Umbraco's component/support-system catalog used to mean a
/// static <c>ComponentTypeRegistry.Register&lt;T&gt;</c> call, a paired
/// <c>GovUkComponentRenderer.RegisterComponent</c>/<c>RegisterField</c> call, and (for the REST/MCP
/// authoring surface) a third <c>AddServiceBlueprintAuthoringApi()</c> call — three separate,
/// order-sensitive registrations with no compile-time or startup-time check a host did all three.
/// <see cref="WayfinderCatalogExtensionHandler"/> now runs every discovered
/// <see cref="IWayfinderCatalogExtension"/> automatically at startup.
/// </summary>
public sealed class WayfinderCatalogExtensionHandlerTests
{
    private sealed class RecordingExtension : IWayfinderCatalogExtension
    {
        public int CallCount { get; private set; }
        public GovUkComponentRenderer? RendererPassed { get; private set; }

        public void Register(GovUkComponentRenderer renderer)
        {
            CallCount++;
            RendererPassed = renderer;
        }
    }

    [Fact]
    public void Handle_RegistersEveryDiscoveredExtension_ExactlyOnce()
    {
        var first = new RecordingExtension();
        var second = new RecordingExtension();
        var renderer = new GovUkComponentRenderer();
        var handler = new WayfinderCatalogExtensionHandler([first, second], renderer);

        handler.Handle(new UmbracoApplicationStartedNotification(isRestarting: false));

        first.CallCount.Should().Be(1);
        second.CallCount.Should().Be(1);
        first.RendererPassed.Should().BeSameAs(renderer);
    }

    [Fact]
    public void Handle_DoesNothing_WhenNoExtensionsAreRegistered()
    {
        var handler = new WayfinderCatalogExtensionHandler([], new GovUkComponentRenderer());

        var act = () => handler.Handle(new UmbracoApplicationStartedNotification(isRestarting: false));

        act.Should().NotThrow();
    }
}
