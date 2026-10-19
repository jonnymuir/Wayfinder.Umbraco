using FluentAssertions;
using Microsoft.Extensions.Options;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Stores;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Emails;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Emails;

/// <summary>
/// A request's move queues each email configured for its blueprint and that move, and only those. A
/// queued email holds ids and counts, never what anyone wrote or their address, and its conversation is
/// shown as it was at the move, so a later message is never quoted as the one being answered.
/// </summary>
public sealed class TransitionEmailTests
{
    private sealed class RecordingQueue : IWayfinderEmailQueue
    {
        public List<WayfinderEmailJob> Jobs { get; } = [];

        public void Enqueue(WayfinderEmailJob job) => Jobs.Add(job);
    }

    private static readonly DateTimeOffset Then = new(2026, 10, 19, 5, 0, 0, TimeSpan.Zero);

    private static ConversationEntry Entry(string queue, string text, int minutes) => new() { QueueKey = queue, Text = text, At = Then.AddMinutes(minutes) };

    private static ServiceRequest Request(params ConversationEntry[] thread) => new()
    {
        InstanceId = "instance-1", BlueprintKey = "contact", TenantId = "default", UserId = "visitor:abc",
        FieldValues = new() { ["name"] = "Jo Bloggs", ["email"] = "jo@example.com", ["conversation"] = thread.ToList() },
    };

    private static WayfinderEmail Email(string name, string blueprint, params string[] actions) => new()
    {
        Name = name, BlueprintKey = blueprint, Actions = actions, Subject = "s", Template = "~/t.cshtml", ToField = "email",
    };

    private static RecordingQueue Move(string action, ServiceRequest request, params WayfinderEmail[] emails)
    {
        var options = new WayfinderEmailOptions();
        options.Emails.AddRange(emails);
        var store = new InMemoryServiceRequestStore();
        store.Save(request);
        var queue = new RecordingQueue();
        new TransitionEmailHandler(Options.Create(options), store, queue)
            .HandleAsync(new ServiceRequestTransitionedNotification(new AuditEvent
            {
                EventId = "e1", InstanceId = request.InstanceId, EventType = AuditEventType.Transition, Actor = "member:1",
                Action = action, OccurredAt = Then, Severity = AuditEventSeverity.Info,
            }), CancellationToken.None)
            .GetAwaiter().GetResult();
        return queue;
    }

    [Fact]
    public void AMove_QueuesEachEmailForItsBlueprintAndAction_AndNoOthers()
    {
        var queue = Move("reply", Request(Entry("visitor", "Hi", 0), Entry("team", "Hello", 5)),
            Email("reply", "contact", "reply", "close"),
            Email("team-notice", "contact", "send"),
            Email("other-service", "licence", "reply"));

        queue.Jobs.Should().ContainSingle().Which.EmailName.Should().Be("reply");
    }

    [Fact]
    public void AQueuedEmail_HoldsNothingAnyoneWrote_NorTheirAddress()
    {
        var job = Move("reply", Request(Entry("visitor", "My secret question", 0)), Email("reply", "contact", "reply")).Jobs.Single();

        var serialized = System.Text.Json.JsonSerializer.Serialize(job);
        serialized.Should().NotContain("My secret question").And.NotContain("jo@example.com").And.NotContain("Jo Bloggs");
        (job.InstanceId, job.Action).Should().Be(("instance-1", "reply"));
    }

    [Fact]
    public void TheConversation_IsShownAsItWasAtTheMove()
    {
        var job = Move("reply", Request(Entry("visitor", "Hi", 0), Entry("team", "Our reply", 5)), Email("reply", "contact", "reply")).Jobs.Single();
        var later = Request(Entry("visitor", "Hi", 0), Entry("team", "Our reply", 5), Entry("visitor", "Written after the reply", 9));

        var model = new WayfinderEmailModel(later, job.Action, job.ConversationLengths, "https://example.com/", null, TimeSpan.Zero);

        model.LatestMessage("conversation")!.Text.Should().Be("Our reply");
        model.Conversation("conversation").Should().HaveCount(2);
        model.PublicBaseUrl.Should().Be("https://example.com");
    }

    [Fact]
    public void AConversationWithNothingAtTheMove_HasNoLatestMessage()
    {
        var model = new WayfinderEmailModel(Request(Entry("visitor", "Hi", 0)), "close", new Dictionary<string, int>(), "https://example.com", null, TimeSpan.Zero);

        model.LatestMessage("conversation").Should().BeNull();
    }
}
