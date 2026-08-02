namespace Wayfinder.Umbraco.Configuration;

/// <summary>
/// Configuration options for the Wayfinder service design engine.
/// Bind from configuration section "Wayfinder".
/// </summary>
public class WayfinderServiceDesignOptions
{
    /// <summary>
    /// How long a workflow step nonce remains valid in the distributed cache.
    /// Defaults to 2 hours. Increase for slow multi-step workflows.
    /// </summary>
    public TimeSpan NonceExpiry { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Base route the built-in <c>file-upload</c>/<c>summary-list</c> partials build their
    /// async-upload and download links against — <c>{base}/upload/{instanceId}/{fieldKey}</c>
    /// and <c>{base}/files/{instanceId}/{fieldKey}</c>. Deliberately NOT hardcoded to a specific
    /// controller here: file upload/download needs an ownership check (does this actor own this
    /// instance?), and Wayfinder.Umbraco carries no access-control opinion of its own (see
    /// SingleQueueStructuralValidator's remarks on the same theme) — a host owns its own
    /// upload/download controllers (mirroring <c>ServiceRequestPageController{T}</c>'s own
    /// generic file-save call in <c>HandlePost</c>, which needs no ownership check because it's
    /// already scoped to the instance the current page render resolved) and only needs to change
    /// this if its controllers aren't mounted at the default. Defaults to "/service-request" —
    /// the convention this package's own reference host (UmbracoPrism.TestSite) uses.
    /// </summary>
    public string FileEndpointBasePath { get; set; } = "/service-request";
}
