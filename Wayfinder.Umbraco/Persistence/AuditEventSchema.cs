using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Database schema for the wayfinderAuditEvent table: one row per engine audit event, backing
/// <see cref="Services.UmbracoAuditLogStore"/>. Rows are append-only and outlive the request
/// they describe; <see cref="Configuration.WayfinderServiceDesignOptions.AuditRetention"/> decides
/// how long they are kept.
/// </summary>
[TableName("wayfinderAuditEvent")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class AuditEventSchema
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("EventId")]
    [Length(64)]
    [Index(IndexTypes.UniqueNonClustered)]
    public string EventId { get; set; } = string.Empty;

    [Column("InstanceId")]
    [Length(64)]
    public string InstanceId { get; set; } = string.Empty;

    [Column("CursorId")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? CursorId { get; set; }

    [Column("EventType")]
    [Length(32)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>The user id that caused the event, or <c>system</c>.</summary>
    [Column("Actor")]
    [Length(450)]
    public string Actor { get; set; } = string.Empty;

    [Column("FromStageKey")]
    [Length(200)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? FromStageKey { get; set; }

    [Column("ToStageKey")]
    [Length(200)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ToStageKey { get; set; }

    [Column("Action")]
    [Length(200)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Action { get; set; }

    [Column("Detail")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Detail { get; set; }

    /// <summary>The <c>AuditEventSeverity</c> value, stored as its number so "at least this
    /// severity" is a plain comparison.</summary>
    [Column("Severity")]
    public int Severity { get; set; }

    [Column("OccurredUtc")]
    public DateTime OccurredUtc { get; set; }
}
