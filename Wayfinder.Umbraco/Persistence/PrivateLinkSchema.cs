using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Database schema for the wayfinderPrivateLink table, backing <see cref="Visitors.UmbracoPrivateLinkStore"/>:
/// one row per private link issued in an email. Only a SHA-256 hash of each link's token is kept, so the
/// table on its own opens nothing.
/// </summary>
[TableName("wayfinderPrivateLink")]
[PrimaryKey("TokenHash", AutoIncrement = false)]
[ExplicitColumns]
public class PrivateLinkSchema
{
    [Column("TokenHash")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    [Length(64)]
    public string TokenHash { get; set; } = string.Empty;

    [Column("InstanceId")]
    [Length(64)]
    [Index(IndexTypes.NonClustered)]
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>The site path the request's stage is on, where the link takes the person.</summary>
    [Column("PagePath")]
    [Length(512)]
    public string PagePath { get; set; } = string.Empty;

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("ExpiresUtc")]
    [Index(IndexTypes.NonClustered)]
    public DateTime ExpiresUtc { get; set; }
}
