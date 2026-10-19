using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Stores;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Services.Sanitization;
using Wayfinder.Umbraco.Services;
using IEventAggregator = global::Umbraco.Cms.Core.Events.IEventAggregator;
using INotification = global::Umbraco.Cms.Core.Notifications.INotification;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// Every saved move publishes a <see cref="ServiceRequestTransitionedNotification"/>, which is how a
/// host reacts to one (a reply email, say). That includes a caseworker moving a request into a queue
/// they cannot see, whose own response reads as access denied although the move landed. A rejected
/// move publishes nothing, and a handler that throws never undoes the move.
/// </summary>
public sealed class ServiceRequestTransitionedNotificationTests
{
    private const string TenantId = "default";

    private static readonly ActorProfile Visitor = new()
    {
        VisibleQueues = ["visitor"], StartableQueues = ["visitor"], ActionableQueues = ["visitor"], RestrictToInstanceOwner = true
    };

    private static readonly ActorProfile Team = new()
    {
        VisibleQueues = ["team"], StartableQueues = [], ActionableQueues = ["team"], RestrictToInstanceOwner = false
    };

    private const string Blueprint = """
        {
          "definitionKey": "contact", "displayName": "Contact us", "version": 1, "initialStage": "write",
          "queues": [ { "key": "visitor", "displayName": "Visitor", "actor": "citizen" }, { "key": "team", "displayName": "Team", "actor": "caseworker" } ],
          "stages": [
            { "stageKey": "write", "displayName": "Write", "queueKey": "visitor",
              "components": [ { "type": "text", "fieldKey": "message", "label": "Message", "required": true } ],
              "routes": [ { "id": "write--send--respond", "target": "respond", "trigger": "send" } ] },
            { "stageKey": "respond", "displayName": "Respond", "queueKey": "team",
              "components": [ { "type": "text", "fieldKey": "answer", "label": "Answer" } ],
              "routes": [ { "id": "respond--reply--write", "target": "write", "trigger": "reply" } ] }
          ]
        }
        """;

    private static (UmbracoProcessManagerEngine Engine, List<ServiceRequestTransitionedNotification> Published) Build(bool handlerThrows = false)
    {
        var published = new List<ServiceRequestTransitionedNotification>();
        var aggregator = new Mock<IEventAggregator>();
        aggregator.Setup(a => a.Publish(It.IsAny<INotification>()))
            .Callback<INotification>(n =>
            {
                published.Add((ServiceRequestTransitionedNotification)n);
                if (handlerThrows)
                {
                    throw new InvalidOperationException("SMTP is down");
                }
            });

        var definition = JsonSerializer.Deserialize<ServiceBlueprint>(Blueprint, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var engine = new UmbracoProcessManagerEngine(
            NullLogger<UmbracoProcessManagerEngine>.Instance,
            new SingleDefinitionServiceBlueprintStore(definition),
            new PassthroughContentSanitizer(),
            new InMemoryServiceRequestStore(),
            new InMemoryAuditLogStore(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            supportSystemClients: [],
            requestConcurrencyPolicies: [],
            eventAggregator: aggregator.Object);
        return (engine, published);
    }

    private static string Sent(UmbracoProcessManagerEngine engine)
    {
        var started = engine.GetCurrent("contact", TenantId, "visitor-1", Visitor);
        engine.Advance(started.InstanceId, TenantId, "visitor-1", Visitor, "send", started.StateVersion,
            new Dictionary<string, object?> { ["message"] = "Hello" });
        return started.InstanceId;
    }

    [Fact]
    public void ASubmittedStage_PublishesItsMove()
    {
        var (engine, published) = Build();

        var instanceId = Sent(engine);

        var move = published.Should().ContainSingle().Subject.Transition;
        move.InstanceId.Should().Be(instanceId);
        (move.FromStageKey, move.ToStageKey, move.Action, move.Actor).Should().Be(("write", "respond", "send", "visitor-1"));
    }

    [Fact]
    public void ACaseworkersMoveIntoAQueueTheyCannotSee_Publishes_AndTheirAnswerIsMovedOn()
    {
        var (engine, published) = Build();
        var instanceId = Sent(engine);
        var item = engine.GetQueueWorkItems(TenantId, "nick", Team).Items.Single(i => i.InstanceId == instanceId);
        var pickedUp = engine.PickupWorkItem(instanceId, item.CursorId, TenantId, "nick", Team);

        var response = engine.Advance(instanceId, TenantId, "nick", Team, "reply", pickedUp.StateVersion,
            new Dictionary<string, object?> { ["answer"] = "Hi" });

        response.ResponseState.Should().Be("moved_on", "the request is now in a queue Nick cannot see");
        published.Select(n => (n.Transition.ToStageKey, n.Transition.Action)).Should().Contain(("write", "reply"));
    }

    [Fact]
    public void ARejectedSubmission_PublishesNothing()
    {
        var (engine, published) = Build();
        var started = engine.GetCurrent("contact", TenantId, "visitor-1", Visitor);

        engine.Advance(started.InstanceId, TenantId, "visitor-1", Visitor, "send", started.StateVersion,
            new Dictionary<string, object?> { ["message"] = "" });

        published.Should().BeEmpty();
    }

    [Fact]
    public void AHandlerThatThrows_NeverUndoesTheMove()
    {
        var (engine, _) = Build(handlerThrows: true);

        var instanceId = Sent(engine);

        engine.GetQueueWorkItems(TenantId, "nick", Team).Items.Should().Contain(i => i.InstanceId == instanceId,
            "the move was saved before the handler ran");
    }
}
