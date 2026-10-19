using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Database schema for the wayfinderVisitorDevice table, backing <see cref="Visitors.UmbracoVisitorDeviceStore"/>:
/// one row per browser an anonymous visitor uses, mapping a SHA-256 hash of that browser's cookie secret
/// to the visitor. The secret itself is never stored.
/// </summary>
[TableName("wayfinderVisitorDevice")]
[PrimaryKey("SecretHash", AutoIncrement = false)]
[ExplicitColumns]
public class VisitorDeviceSchema
{
    [Column("SecretHash")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    [Length(64)]
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>The visitor's id: random, and not a secret (it is what their requests record as their owner).</summary>
    [Column("VisitorId")]
    [Length(64)]
    [Index(IndexTypes.NonClustered)]
    public string VisitorId { get; set; } = string.Empty;

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("LastSeenUtc")]
    [Index(IndexTypes.NonClustered)]
    public DateTime LastSeenUtc { get; set; }
}
