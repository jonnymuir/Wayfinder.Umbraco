using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Umbraco.Persistence;

namespace Wayfinder.Umbraco.Visitors;

/// <summary><see cref="IPrivateLinkStore"/> backed by the wayfinderPrivateLink table.</summary>
public sealed class UmbracoPrivateLinkStore(
    IUmbracoDatabaseFactory databaseFactory,
    IOptions<WayfinderVisitorOptions> options,
    TimeProvider timeProvider) : IPrivateLinkStore
{
    // A token is 32 random bytes, base64url without padding: 43 characters.
    private const int TokenLength = 43;

    public string Issue(string instanceId, string pagePath)
    {
        if (!WayfinderVisitors.IsLocalPath(pagePath))
        {
            throw new ArgumentException("A private link's page must be a path on this site, such as \"/contact-us/\".", nameof(pagePath));
        }

        var token = WayfinderVisitors.RandomSecret();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        using var db = databaseFactory.CreateDatabase();
        db.Execute("DELETE FROM wayfinderPrivateLink WHERE ExpiresUtc < @0", now);
        db.Insert(new PrivateLinkSchema
        {
            TokenHash = Hash(token),
            InstanceId = instanceId,
            PagePath = pagePath,
            CreatedUtc = now,
            ExpiresUtc = now.Add(options.Value.PrivateLinkLifetime),
        });
        return token;
    }

    public PrivateLink? Redeem(string? token)
    {
        if (token is not { Length: TokenLength })
        {
            return null;
        }

        using var db = databaseFactory.CreateDatabase();
        var row = db.SingleOrDefault<PrivateLinkSchema>(
            "SELECT * FROM wayfinderPrivateLink WHERE TokenHash = @0 AND ExpiresUtc > @1",
            Hash(token), timeProvider.GetUtcNow().UtcDateTime);
        return row is null ? null : new PrivateLink(row.InstanceId, row.PagePath);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
