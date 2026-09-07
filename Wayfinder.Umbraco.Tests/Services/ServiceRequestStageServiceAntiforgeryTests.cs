using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Models.ServiceDesign.Components;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// REGRESSION: <see cref="ServiceRequestStageService.RenderCurrentAsync"/> mints an antiforgery
/// token (which writes a Set-Cookie) only to let a bulk-data-review component's fetch calls carry
/// it. Calling <see cref="IAntiforgery.GetAndStoreTokens"/> on <em>every</em> stage render was a
/// live regression — its cookie side effect broke the "waiting" page's own poll request. It must
/// run only when the rendered stage actually contains a bulk-data-review component.
/// </summary>
public sealed class ServiceRequestStageServiceAntiforgeryTests
{
    private static (ServiceRequestStageService Service, Mock<IAntiforgery> Antiforgery) BuildService(
        IReadOnlyList<ComponentRenderPayload> components)
    {
        var envelope = new ServiceRequestResponseEnvelope
        {
            InstanceId = "instance-1",
            ResponseState = "render",
            StateVersion = 1,
            CorrelationId = "c1",
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Render = new StepContent
            {
                StepType = "question",
                StateDisplayName = "A step",
                Components = components,
                AvailableActions = [],
            },
        };

        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.GetCurrent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ActorProfile>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(envelope);

        var antiforgery = new Mock<IAntiforgery>();
        antiforgery
            .Setup(a => a.GetAndStoreTokens(It.IsAny<HttpContext>()))
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "__RequestVerificationToken", null));

        var nonce = new Mock<IStageNonceService>();
        nonce.Setup(n => n.CreateAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FieldRenderPayload>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("nonce-1");

        var service = new ServiceRequestStageService(
            processManager.Object,
            Options.Create(new WayfinderServiceDesignOptions { ResolveUserId = _ => "user-a" }),
            nonce.Object,
            Mock.Of<Wayfinder.Umbraco.Services.IServiceRequestFileStorage>(),
            Mock.Of<IUploadTokenService>(),
            antiforgery.Object,
            NullLogger<ServiceRequestStageService>.Instance);

        return (service, antiforgery);
    }

    [Fact]
    public async Task RenderCurrentAsync_DoesNotTouchAntiforgery_WhenTheStageHasNoBulkDataReviewComponent()
    {
        var (service, antiforgery) = BuildService(
        [
            new ComponentRenderPayload { Type = "waiting" },
            new ComponentRenderPayload { Type = "fieldset" },
        ]);

        await service.RenderCurrentAsync(new DefaultHttpContext(), "bp", "instance-1", null);

        antiforgery.Verify(a => a.GetAndStoreTokens(It.IsAny<HttpContext>()), Times.Never,
            "no bulk-data-review component means nothing needs the token — and GetAndStoreTokens has a Set-Cookie side effect");
    }

    [Fact]
    public async Task RenderCurrentAsync_MintsAndStampsTheToken_WhenTheStageHasABulkDataReviewComponent()
    {
        var (service, antiforgery) = BuildService(
        [
            new ComponentRenderPayload { Type = "bulk-data-review", DatasetId = "dataset-1" },
        ]);

        var result = await service.RenderCurrentAsync(new DefaultHttpContext(), "bp", "instance-1", null);

        antiforgery.Verify(a => a.GetAndStoreTokens(It.IsAny<HttpContext>()), Times.Once);
        result.Envelope.Render!.Components.Single().BulkDatasetAntiforgeryToken.Should().Be("request-token");
    }

    [Fact]
    public async Task RenderCurrentAsync_DoesNotMintAToken_ForABulkDataReviewComponentWithNoDatasetYet()
    {
        var (service, antiforgery) = BuildService(
        [
            new ComponentRenderPayload { Type = "bulk-data-review", DatasetId = null },
        ]);

        await service.RenderCurrentAsync(new DefaultHttpContext(), "bp", "instance-1", null);

        antiforgery.Verify(a => a.GetAndStoreTokens(It.IsAny<HttpContext>()), Times.Never,
            "nothing ingested yet — the component renders its own placeholder and makes no fetch calls");
    }
}
