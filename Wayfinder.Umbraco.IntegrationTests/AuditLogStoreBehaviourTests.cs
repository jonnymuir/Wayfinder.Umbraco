using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// The package's audit store is the one a host gets by default, and it keeps a request's audit
/// trail in the database: what the engine records can be read back, filtered and paged, and is
/// removed only once it is past retention. Each test uses its own instance id, so none depends
/// on what else the booted host recorded.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class AuditLogStoreBehaviourTests(ReferenceAppFactory factory)
{
    private IAuditLogStore Store => factory.Services.GetRequiredService<IAuditLogStore>();

    private static AuditEvent Event(
        string instanceId, DateTimeOffset at, AuditEventSeverity severity = AuditEventSeverity.Info, string actor = "user-1") => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        InstanceId = instanceId,
        CursorId = "cursor-1",
        EventType = AuditEventType.Transition,
        Actor = actor,
        FromStageKey = "start",
        ToStageKey = "done",
        Action = "submit",
        Detail = new string('x', 2000),
        Severity = severity,
        OccurredAt = at,
    };

    private static string NewInstanceId() => $"audit-probe-{Guid.NewGuid():N}";

    [Fact]
    public void AHostThatRegistersNothing_GetsTheDatabaseBackedStore() =>
        Store.Should().BeOfType<UmbracoAuditLogStore>();

    [Fact]
    public void ARecordedEvent_ReadsBackUnchanged_OldestFirst()
    {
        var instanceId = NewInstanceId();
        var now = DateTimeOffset.UtcNow;
        var later = Event(instanceId, now.AddMinutes(1));
        var earlier = Event(instanceId, now);

        Store.Record(later);
        Store.Record(earlier);

        var events = Store.GetByInstance(instanceId);
        events.Select(e => e.EventId).Should().Equal(earlier.EventId, later.EventId);
        events[0].Should().BeEquivalentTo(earlier, o => o.Using<DateTimeOffset>(
            c => c.Subject.Should().BeCloseTo(c.Expectation, TimeSpan.FromMilliseconds(10))).WhenTypeIs<DateTimeOffset>());
    }

    [Fact]
    public void AQuery_FiltersBySeverityAndActor_NewestFirst_AndPages()
    {
        var instanceId = NewInstanceId();
        var now = DateTimeOffset.UtcNow;
        var info = Event(instanceId, now);
        var warning = Event(instanceId, now.AddMinutes(1), AuditEventSeverity.Warning);
        var error = Event(instanceId, now.AddMinutes(2), AuditEventSeverity.Error);
        var otherActor = Event(instanceId, now.AddMinutes(3), AuditEventSeverity.Error, actor: "user-2");
        foreach (var e in new[] { info, warning, error, otherActor })
        {
            Store.Record(e);
        }

        Store.Query(instanceId: instanceId, actor: "user-1", minimumSeverity: AuditEventSeverity.Warning)
            .Select(e => e.EventId).Should().Equal(error.EventId, warning.EventId);
        Store.Query(instanceId: instanceId, pageIndex: 1, pageSize: 3)
            .Select(e => e.EventId).Should().Equal(info.EventId);
    }

    [Fact]
    public void RetentionRemovesOnlyEventsOlderThanTheCutoff()
    {
        var instanceId = NewInstanceId();
        var now = DateTimeOffset.UtcNow;
        var old = Event(instanceId, now.AddDays(-400));
        var recent = Event(instanceId, now.AddDays(-10));
        Store.Record(old);
        Store.Record(recent);

        ((UmbracoAuditLogStore)Store).RemoveOlderThan(now.AddDays(-365));

        Store.GetByInstance(instanceId).Select(e => e.EventId).Should().Equal(recent.EventId);
    }
}
