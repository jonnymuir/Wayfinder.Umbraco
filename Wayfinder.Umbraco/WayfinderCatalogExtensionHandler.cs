using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Wayfinder.Rendering.GovUk;
using Wayfinder.Umbraco.Extensions;

namespace Wayfinder.Umbraco;

/// <summary>
/// Runs every discovered <see cref="IWayfinderCatalogExtension"/> at startup — see that
/// interface's own remarks for what registering one buys a host over the three-separate-manual-calls
/// convention it replaces.
/// </summary>
public class WayfinderCatalogExtensionHandler(
    IEnumerable<IWayfinderCatalogExtension> extensions,
    GovUkComponentRenderer renderer) : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        foreach (var extension in extensions)
        {
            extension.Register(renderer);
        }
    }
}
