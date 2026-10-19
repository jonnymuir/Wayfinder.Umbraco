namespace Wayfinder.Umbraco.Visitors;

/// <summary>
/// The browsers each anonymous visitor uses, by a hash of each one's cookie secret. A visitor can have
/// several (the phone they wrote from, the laptop a private link opened), each with its own secret.
/// </summary>
public interface IVisitorDeviceStore
{
    /// <summary>The visitor a browser's secret hash belongs to, or null.</summary>
    string? VisitorIdFor(string secretHash);

    void Add(string secretHash, string visitorId);

    void Remove(string secretHash);

    /// <summary>Records that the browser was used now, so it is kept while in use.</summary>
    void Touch(string secretHash);

    /// <summary>Removes browsers not used since <paramref name="cutoffUtc"/> and returns how many.</summary>
    int RemoveUnusedSince(DateTime cutoffUtc);
}
