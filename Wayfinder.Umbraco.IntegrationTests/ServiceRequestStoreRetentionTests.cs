using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Engine.Models;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// SECURITY REGRESSION (availability: data loss). An anonymous visitor's request expires with their
/// session only while it is still a draft in their own hands. Once it has been handed over to another
/// queue (a contact message waiting in a team's worklist, say), or belongs to a signed-in member, the
/// database store keeps it. Exercised against the real table, with a sliding window of a few
/// milliseconds so expiry can be observed without waiting for the real thirty minutes.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class ServiceRequestStoreRetentionTests(ReferenceAppFactory factory)
{
    private static readonly TimeSpan SessionWindow = TimeSpan.FromMilliseconds(5);

    private UmbracoServiceRequestStore StoreWithAShortSession() =>
        new(factory.Services.GetRequiredService<IUmbracoDatabaseFactory>(), SessionWindow);

    private static ServiceRequest Request(bool authenticated = false, DateTimeOffset? handedOverAt = null) => new()
    {
        InstanceId = $"retention-probe-{Guid.NewGuid():N}",
        BlueprintKey = "contact",
        TenantId = "default",
        UserId = authenticated ? "member@example.test" : $"anonymous-{Guid.NewGuid():N}",
        IsAuthenticated = authenticated,
        HandedOverAt = handedOverAt,
        CurrentStage = handedOverAt is null ? "message" : "reply",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static void LetTheSessionWindowPass() => Thread.Sleep(SessionWindow * 20);

    [Fact]
    public void AnAnonymousDraft_ExpiresWithTheVisitorsSession()
    {
        var store = StoreWithAShortSession();
        var draft = Request();
        store.Save(draft);

        LetTheSessionWindowPass();

        store.TryGet(draft.InstanceId, out _).Should().BeFalse();
        store.GetAll().Should().NotContain(r => r.InstanceId == draft.InstanceId);
    }

    [Fact]
    public void AnAnonymousRequestHandedOverToAnotherQueue_OutlivesTheVisitorsSession()
    {
        var store = StoreWithAShortSession();
        var submitted = Request(handedOverAt: DateTimeOffset.UtcNow);
        store.Save(submitted);

        LetTheSessionWindowPass();

        store.TryGet(submitted.InstanceId, out var kept).Should().BeTrue(
            "someone else is now responsible for it; deleting it with the visitor's session loses their message");
        kept.HandedOverAt.Should().NotBeNull();
        store.GetAll().Should().Contain(r => r.InstanceId == submitted.InstanceId);
    }

    [Fact]
    public void AnAnonymousRequestHandedOver_StaysKept_AfterTheVisitorReadsItAgain()
    {
        // A read refreshes a draft's window; it must never pull a kept request back onto one.
        var store = StoreWithAShortSession();
        var submitted = Request(handedOverAt: DateTimeOffset.UtcNow);
        store.Save(submitted);
        store.TryGet(submitted.InstanceId, out _).Should().BeTrue();

        LetTheSessionWindowPass();

        store.TryGet(submitted.InstanceId, out _).Should().BeTrue();
    }

    [Fact]
    public void ASignedInMembersRequest_OutlivesTheSessionWindow()
    {
        var store = StoreWithAShortSession();
        var memberRequest = Request(authenticated: true);
        store.Save(memberRequest);

        LetTheSessionWindowPass();

        store.TryGet(memberRequest.InstanceId, out _).Should().BeTrue();
    }
}
