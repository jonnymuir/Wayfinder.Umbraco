using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Controllers;

namespace Wayfinder.Umbraco.Tests.Controllers;

/// <summary>
/// REGRESSION: <see cref="WayfinderServiceDesignOptions.ResolveAccessProfile"/> used to be
/// <c>Func&lt;HttpContext, ActorProfile&gt;</c> — no way for a host to know which blueprint a call
/// was about without reverse-engineering it from the raw request's own path/form/query shape. A
/// host that did exactly that recognised the page GET and the stage-advance POST's own shapes but
/// never this poll endpoint's, so a signed-in applicant's own wait-screen poll silently resolved
/// the wrong access profile and 404'd on every attempt. The resolver now takes the blueprintKey
/// directly — this pins down that <see cref="ServiceRequestPollController"/> actually passes its
/// own <c>[FromQuery] blueprintKey</c> through, not a stale/empty value.
/// </summary>
public sealed class ServiceRequestPollControllerTests
{
    [Fact]
    public void Poll_PassesItsOwnBlueprintKey_ToResolveAccessProfile()
    {
        string? observedBlueprintKey = "not-called";
        var options = Options.Create(new WayfinderServiceDesignOptions
        {
            ResolveTenantId = _ => "tenant-a",
            ResolveUserId = _ => "user-a",
            ResolveAccessProfile = (_, blueprintKey) =>
            {
                observedBlueprintKey = blueprintKey;
                return new ActorProfile();
            },
        });

        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.GetCurrent(
                "apply-for-a-juggling-licence", "tenant-a", "user-a", It.IsAny<ActorProfile>(),
                "instance-1", null))
            .Returns(new ServiceRequestResponseEnvelope
            {
                InstanceId = "instance-1",
                ResponseState = "render",
                StateVersion = 4,
                CorrelationId = "correlation-1",
                ServerTimeUtc = DateTimeOffset.UtcNow,
            });

        var controller = new ServiceRequestPollController(processManager.Object, options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        controller.Poll("apply-for-a-juggling-licence", "instance-1", knownStateVersion: 4);

        observedBlueprintKey.Should().Be("apply-for-a-juggling-licence",
            "the poll endpoint's own blueprintKey query parameter must reach the host's resolver, not get dropped");
    }
}
