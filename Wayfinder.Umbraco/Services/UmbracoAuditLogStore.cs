using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Umbraco.Persistence;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// <see cref="IAuditLogStore"/> backed by the wayfinderAuditEvent table, so a request's audit
/// trail survives a restart and can be read back, like the request itself
/// (<see cref="UmbracoServiceRequestStore"/>). Append-only; <see cref="ServiceRequestSweepService"/>
/// removes events older than <c>Wayfinder:AuditRetention</c>.
/// </summary>
public sealed class UmbracoAuditLogStore(IUmbracoDatabaseFactory databaseFactory) : IAuditLogStore
{
    private const int MaxPageSize = 500;

    public void Record(AuditEvent auditEvent)
    {
        using var db = databaseFactory.CreateDatabase();
        db.Insert(new AuditEventSchema
        {
            EventId = auditEvent.EventId,
            InstanceId = auditEvent.InstanceId,
            CursorId = auditEvent.CursorId,
            EventType = auditEvent.EventType.ToString(),
            Actor = auditEvent.Actor,
            FromStageKey = auditEvent.FromStageKey,
            ToStageKey = auditEvent.ToStageKey,
            Action = auditEvent.Action,
            Detail = auditEvent.Detail,
            Severity = (int)auditEvent.Severity,
            OccurredUtc = auditEvent.OccurredAt.UtcDateTime,
        });
    }

    public IReadOnlyList<AuditEvent> GetByInstance(string instanceId)
    {
        using var db = databaseFactory.CreateDatabase();
        return db.Fetch<AuditEventSchema>(
                "SELECT * FROM wayfinderAuditEvent WHERE InstanceId = @0 ORDER BY OccurredUtc, Id", instanceId)
            .Select(ToEvent)
            .ToArray();
    }

    public IReadOnlyList<AuditEvent> Query(
        string? instanceId = null,
        string? actor = null,
        AuditEventSeverity? minimumSeverity = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int pageIndex = 0,
        int pageSize = 50)
    {
        var sql = new Sql("SELECT * FROM wayfinderAuditEvent WHERE 1 = 1");
        AppendIf(sql, instanceId is not null, "AND InstanceId = @0", instanceId);
        AppendIf(sql, actor is not null, "AND Actor = @0", actor);
        AppendIf(sql, minimumSeverity is not null, "AND Severity >= @0", (int?)minimumSeverity);
        AppendIf(sql, fromUtc is not null, "AND OccurredUtc >= @0", fromUtc?.UtcDateTime);
        AppendIf(sql, toUtc is not null, "AND OccurredUtc <= @0", toUtc?.UtcDateTime);
        sql.Append("ORDER BY OccurredUtc DESC, Id DESC");

        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        using var db = databaseFactory.CreateDatabase();
        return db.SkipTake<AuditEventSchema>((long)Math.Max(pageIndex, 0) * take, take, sql)
            .Select(ToEvent)
            .ToArray();
    }

    /// <summary>
    /// Deletes events that occurred before <paramref name="cutoff"/> and returns how many. The
    /// retention sweep calls this; a host can call it for its own retention rules.
    /// </summary>
    public int RemoveOlderThan(DateTimeOffset cutoff)
    {
        using var db = databaseFactory.CreateDatabase();
        return db.Execute("DELETE FROM wayfinderAuditEvent WHERE OccurredUtc < @0", cutoff.UtcDateTime);
    }

    private static void AppendIf(Sql sql, bool condition, string clause, object? value)
    {
        if (condition)
        {
            sql.Append(clause, value);
        }
    }

    private static AuditEvent ToEvent(AuditEventSchema row) => new()
    {
        EventId = row.EventId,
        InstanceId = row.InstanceId,
        CursorId = row.CursorId,
        EventType = Enum.Parse<AuditEventType>(row.EventType),
        Actor = row.Actor,
        FromStageKey = row.FromStageKey,
        ToStageKey = row.ToStageKey,
        Action = row.Action,
        Detail = row.Detail,
        Severity = (AuditEventSeverity)row.Severity,
        OccurredAt = new DateTimeOffset(DateTime.SpecifyKind(row.OccurredUtc, DateTimeKind.Utc)),
    };
}
