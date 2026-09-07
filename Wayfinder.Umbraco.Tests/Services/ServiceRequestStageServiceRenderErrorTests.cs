using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// REGRESSION: <see cref="ServiceRequestStageService.RenderCurrentAsync"/> used to let any
/// unhandled exception (a corrupt stored instance, a calculation bug, a null-resolver mistake)
/// propagate straight out of the citizen-facing Block Grid partial as a raw ASP.NET Core error
/// page. It now renders the same <c>ResponseState == "error"</c> envelope shape the engine's own
/// expected failures already use, so the partial has exactly one error-rendering path.
/// </summary>
public sealed class ServiceRequestStageServiceRenderErrorTests
{
    private static ServiceRequestStageService BuildService(Mock<IProcessManager> processManager) => new(
        processManager.Object,
        Options.Create(new WayfinderServiceDesignOptions
        {
            ResolveUserId = _ => "user-a",
        }),
        Mock.Of<IStageNonceService>(),
        Mock.Of<Wayfinder.Umbraco.Services.IServiceRequestFileStorage>(),
        Mock.Of<IUploadTokenService>(),
        Mock.Of<Microsoft.AspNetCore.Antiforgery.IAntiforgery>(),
        NullLogger<ServiceRequestStageService>.Instance);

    [Fact]
    public async Task RenderCurrentAsync_ReturnsAnErrorEnvelope_InsteadOfThrowing_WhenTheEngineThrows()
    {
        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.GetCurrent(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("simulated corrupt stored instance"));
        var service = BuildService(processManager);

        ServiceRequestStageRenderResult? result = null;
        var act = async () => result = await service.RenderCurrentAsync(new DefaultHttpContext(), "some-blueprint", null, null);

        await act.Should().NotThrowAsync(
            "an unexpected engine exception must render a GDS error state, not crash the citizen-facing page");

        result!.Envelope.ResponseState.Should().Be("error");
        result!.Envelope.Problems.Should().ContainSingle()
            .Which.Message.Should().NotContain("simulated corrupt stored instance",
                "the raw exception message must never reach the citizen — only a generic, safe message");
    }
}
