namespace Wayfinder.Umbraco;

/// <summary>
/// Keyed-DI service keys this package's own generic controllers look up by convention, so a
/// host and this package agree on the key without either side guessing a string literal.
/// </summary>
public static class WayfinderUmbracoServiceKeys
{
    /// <summary>
    /// <see cref="Controllers.ServiceRequestHubController"/> looks up an
    /// <c>IBusinessAppProcessManagerClient</c> registered under this key to also show a host's
    /// own in-process (no remote business app) queue on the hub, alongside the default unkeyed
    /// client's instances. A host with no in-process queue registers nothing under this key —
    /// the hub controller treats that as "no in-process queue to show", not an error.
    /// </summary>
    public const string InProcessQueueClient = "wayfinder-in-process";
}
