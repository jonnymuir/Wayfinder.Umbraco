using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Tests.Visitors;

/// <summary>In-memory stand-ins for the visitor stores (contracts this package owns), and request helpers.</summary>
internal sealed class InMemoryDevices : IVisitorDeviceStore
{
    public Dictionary<string, string> Rows { get; } = new(StringComparer.Ordinal);

    public string? VisitorIdFor(string secretHash) => Rows.GetValueOrDefault(secretHash);

    public void Add(string secretHash, string visitorId) => Rows[secretHash] = visitorId;

    public void Remove(string secretHash) => Rows.Remove(secretHash);

    public void Touch(string secretHash)
    {
    }

    public int RemoveUnusedSince(DateTime cutoffUtc) => 0;
}

internal sealed class InMemoryLinks : IPrivateLinkStore
{
    public Dictionary<string, PrivateLink> Live { get; } = new(StringComparer.Ordinal);

    public string Issue(string instanceId, string pagePath)
    {
        var token = Guid.NewGuid().ToString("N") + "abcdefghijk";
        Live[token] = new PrivateLink(instanceId, pagePath);
        return token;
    }

    public PrivateLink? Redeem(string? token) => token is not null && Live.TryGetValue(token, out var link) ? link : null;
}

internal static class Visit
{
    public static WayfinderVisitors Visitors(InMemoryDevices devices, Mock<IProcessManager>? engine = null, Func<HttpContext, string?>? signedIn = null) =>
        new(
            Options.Create(new WayfinderVisitorOptions { SignedInUserId = signedIn ?? (_ => null) }),
            devices,
            (engine ?? new Mock<IProcessManager>()).Object,
            new MemoryCache(new MemoryCacheOptions()));

    public static DefaultHttpContext WithCookies(params (string Name, string Value)[] cookies)
    {
        var context = new DefaultHttpContext();
        if (cookies.Length > 0)
        {
            context.Request.Headers.Cookie = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        }

        return context;
    }

    /// <summary>The value this response set for <paramref name="name"/>, or null; empty when it deleted it.</summary>
    public static string? SetCookie(HttpContext context, string name) =>
        SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.OfType<string>().ToList())
            .LastOrDefault(c => c.Name.Value == name) is { } cookie
            ? cookie.Value.Value ?? ""
            : null;

    public static SetCookieHeaderValue? SetCookieHeader(HttpContext context, string name) =>
        SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.OfType<string>().ToList()).LastOrDefault(c => c.Name.Value == name);
}
