namespace Wayfinder.Umbraco.Visitors;

/// <summary>
/// The private links that take someone back to their request from an email, on any device. A link's
/// token is a 256-bit random value; only its hash is stored, and a link stops working once it expires.
/// </summary>
public interface IPrivateLinkStore
{
    /// <summary>Issues a link to <paramref name="instanceId"/> on the page at <paramref name="pagePath"/> and returns its token, which is never stored.</summary>
    string Issue(string instanceId, string pagePath);

    /// <summary>What a token opens, or null for an unknown or expired token.</summary>
    PrivateLink? Redeem(string? token);
}

/// <param name="InstanceId">The request the link opens.</param>
/// <param name="PagePath">The site path the request's stage is on.</param>
public sealed record PrivateLink(string InstanceId, string PagePath);
