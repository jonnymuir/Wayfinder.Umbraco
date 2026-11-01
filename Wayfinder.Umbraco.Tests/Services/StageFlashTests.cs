using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// SECURITY REGRESSION (cross-request disclosure). A failed stage submission leaves its problems and what
/// was typed for the next render, and only a render of that same request may show them. They once rode
/// in TempData with no owner: a team member's reply ("access denied" plus the reply text) turned up on
/// the next stage form that browser opened, a visitor's own conversation. A move that was saved leaves
/// nothing behind at all.
/// </summary>
public sealed class StageFlashTests
{
    private static readonly IReadOnlyList<FieldRenderPayload> Fields =
        [new FieldRenderPayload { FieldKey = "conversation", Label = "Reply", FieldType = "conversation", Required = false }];

    private static readonly StageFlash TeamReplyFlash = new(
        "instance-1",
        [new ServiceRequestProblem { FieldKey = "", Message = "Access denied to the current queue.", Code = "ACCESS_DENIED" }],
        new Dictionary<string, string> { ["conversation"] = "Test reply" });

    private static ServiceRequestResponseEnvelope Envelope(string instanceId, string state = "render") => new()
    {
        InstanceId = instanceId, ResponseState = state, StateVersion = 2, CorrelationId = "c1", ServerTimeUtc = DateTimeOffset.UtcNow,
        Render = state == "render"
            ? new StepContent { StepType = "question", StateDisplayName = "Your conversation", Components = [], AvailableActions = [] }
            : null,
    };

    private static ServiceRequestStageService Build(ServiceRequestResponseEnvelope current, ServiceRequestResponseEnvelope? afterAdvance = null)
    {
        var engine = new Mock<IProcessManager>();
        engine.Setup(p => p.GetCurrent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
            It.IsAny<string?>(), It.IsAny<string?>())).Returns(current);
        engine.Setup(p => p.Advance(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, object?>>())).Returns(afterAdvance ?? current);

        var nonce = new Mock<IStageNonceService>();
        nonce.Setup(n => n.ResolveAsync("nonce-1", "instance-1", "user-a", It.IsAny<CancellationToken>())).ReturnsAsync(Fields);
        nonce.Setup(n => n.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<FieldRenderPayload>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync("nonce-2");

        return new ServiceRequestStageService(
            engine.Object,
            Options.Create(new WayfinderServiceDesignOptions { ResolveUserId = _ => "user-a" }),
            nonce.Object,
            Mock.Of<Wayfinder.Umbraco.Services.IServiceRequestFileStorage>(),
            Mock.Of<IUploadTokenService>(),
            Mock.Of<IAntiforgery>(),
            NullLogger<ServiceRequestStageService>.Instance);
    }

    private static FormCollection Reply() => new FormCollection(new Dictionary<string, StringValues>
    {
        ["ReturnUrl"] = "/messages", ["InstanceId"] = "instance-1", ["Nonce"] = "nonce-1", ["BlueprintKey"] = "contact",
        ["Action"] = "reply", ["StateVersion"] = "1", ["field:conversation"] = "Test reply",
    });

    [Fact]
    public async Task AFlash_IsShownWhenItsOwnRequestIsRendered()
    {
        var result = await Build(Envelope("instance-1")).RenderCurrentAsync(new DefaultHttpContext(), "contact", null, null, TeamReplyFlash);

        result.Problems.Should().ContainSingle(p => p.Code == "ACCESS_DENIED");
        result.FormValues.Should().Contain("conversation", "Test reply");
    }

    [Fact]
    public async Task AFlash_IsNeverShownOnAnotherRequestsForm()
    {
        var result = await Build(Envelope("visitors-own-request")).RenderCurrentAsync(new DefaultHttpContext(), "contact", null, null, TeamReplyFlash);

        result.Problems.Should().BeEmpty();
        result.FormValues.Should().BeEmpty("one request's typed text must never prefill another's form");
    }

    [Fact]
    public async Task AFlash_IsNeverShownOnAnErrorPageForAnotherRequest()
    {
        var result = await Build(Envelope("", state: "error")).RenderCurrentAsync(new DefaultHttpContext(), "contact", "other", null, TeamReplyFlash);

        result.FormValues.Should().BeEmpty();
    }

    [Fact]
    public async Task AReplyThatMovedTheRequestOn_LeavesNothingForTheNextPage()
    {
        var movedOn = new ServiceRequestResponseEnvelope
        {
            InstanceId = "instance-1", ResponseState = "moved_on", StateVersion = 3, CorrelationId = "c1", ServerTimeUtc = DateTimeOffset.UtcNow,
        };

        var result = await Build(Envelope("instance-1"), afterAdvance: movedOn).AdvanceAsync(new DefaultHttpContext(), Reply());

        result.Flash.Should().BeNull();
    }

    [Fact]
    public async Task ARefusedSubmission_LeavesItsProblemsTiedToItsOwnRequest()
    {
        var refused = new ServiceRequestResponseEnvelope
        {
            InstanceId = "", ResponseState = "error", StateVersion = 0, CorrelationId = "c1", ServerTimeUtc = DateTimeOffset.UtcNow,
            Problems = [new ServiceRequestProblem { FieldKey = "", Message = "State version mismatch.", Code = "VERSION_MISMATCH" }],
        };

        var result = await Build(Envelope("instance-1"), afterAdvance: refused).AdvanceAsync(new DefaultHttpContext(), Reply());

        result.Flash!.InstanceId.Should().Be("instance-1");
        result.Flash.FormValues.Should().Contain("conversation", "Test reply");
    }

    [Fact]
    public void AFlash_IsTakenOnce()
    {
        var tempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
        TeamReplyFlash.Keep(tempData);

        StageFlash.Take(tempData)!.InstanceId.Should().Be("instance-1");
        StageFlash.Take(tempData).Should().BeNull();
    }

    [Fact]
    public void AMoveThatMovedTheRequestOn_IsRecognisedOnTheNextPage_EvenAfterTheCookieRoundTrip()
    {
        var instanceId = Guid.NewGuid().ToString();
        var tempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
        StageFlash.KeepMovedOn(tempData, instanceId);
        // The cookie TempData provider hands a value that looks like a GUID back as a Guid.
        tempData["Wayfinder.MovedOn"] = Guid.Parse(instanceId);

        StageFlash.TakeMovedOn(tempData, "another-request").Should().BeFalse();
        StageFlash.KeepMovedOn(tempData, instanceId);
        tempData["Wayfinder.MovedOn"] = Guid.Parse(instanceId);
        StageFlash.TakeMovedOn(tempData, instanceId).Should().BeTrue();
        StageFlash.TakeMovedOn(tempData, instanceId).Should().BeFalse("it is taken once");
    }
}
