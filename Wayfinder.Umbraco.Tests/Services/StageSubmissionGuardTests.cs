using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;
using Wayfinder.Umbraco.TagHelpers;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// SECURITY REGRESSION. A host's <see cref="IStageSubmissionGuard"/> (bot protection, say) decides
/// whether a stage submission may go through: a rejection reaches the person as a message and nothing
/// about the request changes. It is only consulted for a submission whose nonce is genuine, so posts
/// that were never a rendered form cannot make it do work (a CAPTCHA provider call, say). Every stage
/// form carries each guard's fields, and its own hidden values are always attribute-encoded.
/// </summary>
public sealed class StageSubmissionGuardTests
{
    private static readonly IReadOnlyList<FieldRenderPayload> Fields =
        [new FieldRenderPayload { FieldKey = "message", Label = "Message", FieldType = "textarea", Required = true }];

    private sealed class RecordingGuard(string? rejection) : IStageSubmissionGuard
    {
        public int Checks { get; private set; }

        public IHtmlContent? FormFields(StageSubmissionGuardContext context) =>
            new HtmlString($"<input type=\"hidden\" name=\"captcha\" data-blueprint=\"{context.BlueprintKey}\">");

        public Task<string?> CheckAsync(StageSubmissionGuardContext context, IFormCollection form)
        {
            Checks++;
            return Task.FromResult(rejection);
        }
    }

    private static FormCollection Form(string nonce = "nonce-1") => new FormCollection(new Dictionary<string, StringValues>
    {
        ["ReturnUrl"] = "/contact-us",
        ["InstanceId"] = "instance-1",
        ["Nonce"] = nonce,
        ["BlueprintKey"] = "contact",
        ["Action"] = "send",
        ["StateVersion"] = "1",
        ["field:message"] = "Hello",
    });

    private static (ServiceRequestStageService Service, Mock<IProcessManager> Engine) Build(params IStageSubmissionGuard[] guards)
    {
        var engine = new Mock<IProcessManager>();
        engine.Setup(p => p.Advance(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns(new ServiceRequestResponseEnvelope
            {
                InstanceId = "instance-1", ResponseState = "render", StateVersion = 2, CorrelationId = "c1",
                ServerTimeUtc = DateTimeOffset.UtcNow, Problems = [],
            });

        var nonce = new Mock<IStageNonceService>();
        nonce.Setup(n => n.ResolveAsync("nonce-1", "instance-1", "user-a", It.IsAny<CancellationToken>())).ReturnsAsync(Fields);

        var service = new ServiceRequestStageService(
            engine.Object,
            Options.Create(new WayfinderServiceDesignOptions { ResolveUserId = _ => "user-a" }),
            nonce.Object,
            Mock.Of<Wayfinder.Umbraco.Services.IServiceRequestFileStorage>(),
            Mock.Of<IUploadTokenService>(),
            Mock.Of<IAntiforgery>(),
            NullLogger<ServiceRequestStageService>.Instance,
            guards);
        return (service, engine);
    }

    private static void EngineNeverAdvanced(Mock<IProcessManager> engine) =>
        engine.Verify(p => p.Advance(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, object?>>()), Times.Never);

    [Fact]
    public async Task ARejectedSubmission_ChangesNothing_AndReturnsThePersonToTheStageWithTheMessage()
    {
        var (service, engine) = Build(new RecordingGuard("Please confirm you are not a robot."));

        var result = await service.AdvanceAsync(new DefaultHttpContext(), Form());

        EngineNeverAdvanced(engine);
        result.ReturnUrl.Should().Be("/contact-us");
        result.Problems.Should().ContainSingle(p => p.Message == "Please confirm you are not a robot." && p.Code == "SUBMISSION_REJECTED");
        result.FormValues.Should().ContainKey("message", "what they typed comes back with the message");
    }

    [Fact]
    public async Task ASubmissionEveryGuardAllows_GoesThrough()
    {
        var first = new RecordingGuard(null);
        var second = new RecordingGuard(null);
        var (service, engine) = Build(first, second);

        await service.AdvanceAsync(new DefaultHttpContext(), Form());

        first.Checks.Should().Be(1);
        second.Checks.Should().Be(1);
        engine.Verify(p => p.Advance("instance-1", It.IsAny<string>(), "user-a", It.IsAny<ActorProfile>(), "send", 1,
            It.IsAny<Dictionary<string, object?>>()), Times.Once);
    }

    [Fact]
    public async Task TheFirstRejection_StopsTheSubmission()
    {
        var rejecting = new RecordingGuard("No.");
        var later = new RecordingGuard(null);
        var (service, engine) = Build(rejecting, later);

        await service.AdvanceAsync(new DefaultHttpContext(), Form());

        later.Checks.Should().Be(0);
        EngineNeverAdvanced(engine);
    }

    [Fact]
    public async Task AGuard_IsNeverAskedAboutAPostWithoutAGenuineNonce()
    {
        var guard = new RecordingGuard(null);
        var (service, engine) = Build(guard);

        await service.AdvanceAsync(new DefaultHttpContext(), Form(nonce: "forged"));

        guard.Checks.Should().Be(0, "a post that was never a rendered form must not make a guard call out to anything");
        EngineNeverAdvanced(engine);
    }

    private static async Task<string> RenderForm(string returnUrl, params IStageSubmissionGuard[] guards)
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(a => a.GetAndStoreTokens(It.IsAny<HttpContext>()))
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "__RequestVerificationToken", null));
        var tagHelper = new StageFormTagHelper(antiforgery.Object, guards)
        {
            ViewContext = new ViewContext { HttpContext = new DefaultHttpContext() },
            InstanceId = "instance-1", BlueprintKey = "contact", ReturnUrl = returnUrl, Nonce = "nonce-1", StateVersion = 1,
        };
        var output = new TagHelperOutput("wayfinder-stage-form", [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent("<p>fields</p>")));

        await tagHelper.ProcessAsync(new TagHelperContext([], new Dictionary<object, object>(), "id"), output);

        using var writer = new StringWriter();
        output.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public async Task EveryStageForm_CarriesEachGuardsFields_InsideTheForm()
    {
        var html = await RenderForm("/contact-us", new RecordingGuard(null));

        html.Should().Contain("<input type=\"hidden\" name=\"captcha\" data-blueprint=\"contact\">");
        html.IndexOf("name=\"captcha\"", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("</form>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReturnUrlCarryingMarkup_IsAttributeEncoded_NeverBreaksOutOfTheField()
    {
        var html = await RenderForm("/contact-us/\"><script>alert(1)</script>");

        html.Should().NotContain("<script>");
        html.Should().Contain("value=\"/contact-us/&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"");
    }
}
