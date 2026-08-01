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
}
