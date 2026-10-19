using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Visitors;

/// <summary>
/// Who an anonymous visitor is. Each browser they use holds its own random 256-bit secret in an HttpOnly
/// cookie. The visitor's id, which their requests record as their owner (<c>visitor:{id}</c>), is a
/// one-way SHA-256 derivation of the secret of the browser they started on, so anything that shows or
/// stores it (a worklist, the audit log, a database backup) is no way to act as them: only a browser
/// holding a secret is. Another browser a private link adds has a secret of its own, which
/// <see cref="IVisitorDeviceStore"/> maps (by its hash) to the visitor. Nothing is stored for a visitor
/// who only ever uses one browser. A cookie that is not exactly the shape this issues is ignored.
/// </summary>
public sealed partial class WayfinderVisitors(
    IOptions<WayfinderVisitorOptions> options,
    IVisitorDeviceStore devices,
    IProcessManager processManager,
    IMemoryCache cache)
{
    public const string VisitorPrefix = "visitor:";

    // How long a device's visitor id is held in memory; also how often its last use is recorded.
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    private static readonly object ResolvedKey = new();

    private WayfinderVisitorOptions Options => options.Value;

    /// <summary>
    /// The signed-in user's id, otherwise this browser's visitor id, making this browser a new visitor if it
    /// isn't one yet. Answered once per request, so a new visitor is one visitor however often it is asked.
    /// </summary>
    public string ResolveUserId(HttpContext context)
    {
        if (context.Items[ResolvedKey] is string resolved)
        {
            return resolved;
        }

        var userId = Options.SignedInUserId(context) ?? ResolveVisitor(context);
        context.Items[ResolvedKey] = userId;
        return userId;
    }

    private string ResolveVisitor(HttpContext context)
    {
        var secret = PeekSecret(context) ?? RandomSecret();
        WriteCookie(context, Options.CookieName, secret, Options.CookieLifetime);
        return VisitorPrefix + VisitorIdFor(secret);
    }

    /// <summary>This browser's visitor id, if it has a visitor cookie; issues and renews nothing.</summary>
    public string? PeekVisitorUserId(HttpContext context) =>
        PeekSecret(context) is { } secret ? VisitorPrefix + VisitorIdFor(secret) : null;

    /// <summary>
    /// When someone signed in still carries a visitor cookie, moves that visitor's requests onto their
    /// account and forgets this browser as the visitor: what they started before signing in is theirs once
    /// signed in. Only for a person limited to their own requests: a team member's browser claims nothing
    /// and keeps its visitor cookie, so testing the visitor side and then signing in to the team side never
    /// takes a visitor's request into the team member's account.
    /// </summary>
    public void ClaimForSignedInUser(HttpContext context, string tenantId, ActorProfile accessProfile)
    {
        if (!accessProfile.RestrictToInstanceOwner
            || Options.SignedInUserId(context) is not { } signedIn
            || PeekVisitorUserId(context) is not { } visitor)
        {
            return;
        }

        processManager.ClaimInstances(tenantId, visitor, signedIn);
        Forget(context);
    }

    /// <summary>
    /// Makes this browser another of the visitor <paramref name="visitorUserId"/>'s, with a secret of its own,
    /// as a private link that proved the visitor's request does. Their other browsers keep working.
    /// </summary>
    public bool AddThisBrowser(HttpContext context, string visitorUserId)
    {
        if (!IsVisitorUserId(visitorUserId))
        {
            return false;
        }

        if (PeekSecret(context) is { } previous)
        {
            ForgetSecret(previous);
        }

        var secret = RandomSecret();
        devices.Add(Hash(secret), visitorUserId[VisitorPrefix.Length..]);
        WriteCookie(context, Options.CookieName, secret, Options.CookieLifetime);
        context.Items.Remove(ResolvedKey);
        return true;
    }

    /// <summary>Forgets this browser as a visitor: its secret stops working and its cookie is removed.</summary>
    public void Forget(HttpContext context)
    {
        if (PeekSecret(context) is { } secret)
        {
            ForgetSecret(secret);
        }

        context.Response.Cookies.Delete(Options.CookieName, CookieOptions(null));
        context.Items.Remove(ResolvedKey);
    }

    public static bool IsVisitorUserId(string? userId) =>
        userId is not null && userId.StartsWith(VisitorPrefix, StringComparison.Ordinal) && userId.Length > VisitorPrefix.Length;

    /// <summary>A path on this site ("/contact-us/"), never a scheme, host or protocol-relative "//" address.</summary>
    public static bool IsLocalPath(string? path) =>
        path is { Length: > 0 } && path[0] == '/' && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'));

    internal static string RandomSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    internal static void WriteCookie(HttpContext context, string name, string value, TimeSpan lifetime) =>
        context.Response.Cookies.Append(name, value, CookieOptions(DateTimeOffset.UtcNow.Add(lifetime)));

    internal static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        Expires = expires,
    };

    internal static bool IsWellFormedSecret(string? value) => value is not null && SecretShape().IsMatch(value);

    // A browser a private link added maps to its visitor; any other browser is the visitor it started.
    private string VisitorIdFor(string secret)
    {
        var hash = Hash(secret);
        if (cache.TryGetValue(CacheKey(hash), out string? known) && known is not null)
        {
            return known;
        }

        // Only a known browser is remembered: caching misses would let anyone fill memory with made-up cookies.
        if (devices.VisitorIdFor(hash) is { } linked)
        {
            devices.Touch(hash);
            cache.Set(CacheKey(hash), linked, CacheFor);
            return linked;
        }

        return DerivedId(secret);
    }

    private void ForgetSecret(string secret)
    {
        var hash = Hash(secret);
        devices.Remove(hash);
        cache.Remove(CacheKey(hash));
    }

    private static string CacheKey(string hash) => $"Wayfinder.VisitorDevice:{hash}";

    private string? PeekSecret(HttpContext context) =>
        context.Request.Cookies.TryGetValue(Options.CookieName, out var value) && IsWellFormedSecret(value) ? value : null;

    // The visitor id a browser's own secret gives, one way (and apart from the device hash, by its prefix).
    private static string DerivedId(string secret) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes("wayfinder-visitor:" + secret)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // 32 random bytes, base64url without padding.
    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex SecretShape();
}
