using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Umbraco.Persistence;

namespace Wayfinder.Umbraco.Visitors;

/// <summary><see cref="IVisitorDeviceStore"/> backed by the wayfinderVisitorDevice table.</summary>
public sealed class UmbracoVisitorDeviceStore(IUmbracoDatabaseFactory databaseFactory, TimeProvider timeProvider) : IVisitorDeviceStore
{
    public string? VisitorIdFor(string secretHash)
    {
        using var db = databaseFactory.CreateDatabase();
        return db.SingleOrDefault<VisitorDeviceSchema>("SELECT * FROM wayfinderVisitorDevice WHERE SecretHash = @0", secretHash)?.VisitorId;
    }

    public void Add(string secretHash, string visitorId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        using var db = databaseFactory.CreateDatabase();
        db.Insert(new VisitorDeviceSchema { SecretHash = secretHash, VisitorId = visitorId, CreatedUtc = now, LastSeenUtc = now });
    }

    public void Remove(string secretHash)
    {
        using var db = databaseFactory.CreateDatabase();
        db.Execute("DELETE FROM wayfinderVisitorDevice WHERE SecretHash = @0", secretHash);
    }

    public void Touch(string secretHash)
    {
        using var db = databaseFactory.CreateDatabase();
        db.Execute("UPDATE wayfinderVisitorDevice SET LastSeenUtc = @0 WHERE SecretHash = @1", timeProvider.GetUtcNow().UtcDateTime, secretHash);
    }

    public int RemoveUnusedSince(DateTime cutoffUtc)
    {
        using var db = databaseFactory.CreateDatabase();
        return db.Execute("DELETE FROM wayfinderVisitorDevice WHERE LastSeenUtc < @0", cutoffUtc);
    }
}
