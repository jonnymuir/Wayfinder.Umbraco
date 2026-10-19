using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Extensions;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// Times are stored in UTC and shown in the site's display time zone: a message sent at 05:37 UTC in
/// October reads 06:37 to someone in the UK, while it is still British Summer Time.
/// </summary>
public sealed class DisplayTimeZoneTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 10, 19, 5, 37, 0, TimeSpan.Zero);

    private static async Task<ConversationMessagePayload> RenderedMessage(string displayTimeZone)
    {
        var options = Options.Create(new WayfinderServiceDesignOptions { ResolveUserId = _ => "user-a", DisplayTimeZone = displayTimeZone });
        var envelope = new ServiceRequestResponseEnvelope
        {
            InstanceId = "instance-1", ResponseState = "render", StateVersion = 1, CorrelationId = "c1", ServerTimeUtc = DateTimeOffset.UtcNow,
            Render = new StepContent
            {
                StepType = "question", StateDisplayName = "Your conversation", AvailableActions = [],
                Components =
                [
                    new ComponentRenderPayload
                    {
                        Type = "fieldset",
                        Fields =
                        [
                            new FieldRenderPayload
                            {
                                FieldKey = "conversation", Label = "Reply to us", FieldType = "conversation", Required = false,
                                Conversation = [new ConversationMessagePayload { AuthorLabel = "You", Text = "Test message", IsOwnSide = true, At = SentAt }],
                            },
                        ],
                    },
                ],
            },
        };
        var engine = new Mock<IProcessManager>();
        engine.Setup(p => p.GetCurrent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
            It.IsAny<string?>(), It.IsAny<string?>())).Returns(envelope);
        var nonce = new Mock<IStageNonceService>();
        nonce.Setup(n => n.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<FieldRenderPayload>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync("nonce-1");
        var service = new ServiceRequestStageService(
            engine.Object, options, nonce.Object, Mock.Of<Wayfinder.Umbraco.Services.IServiceRequestFileStorage>(),
            Mock.Of<IUploadTokenService>(), Mock.Of<IAntiforgery>(), NullLogger<ServiceRequestStageService>.Instance,
            displayTime: new WayfinderDisplayTime(options));

        var result = await service.RenderCurrentAsync(new DefaultHttpContext(), "contact", null, null);
        return result.Envelope.Render!.Components.Single().Fields.Single().Conversation!.Single();
    }

    [Fact]
    public async Task AMessagesTime_IsShownInTheDisplayTimeZone()
    {
        var message = await RenderedMessage("Europe/London");

        message.At.Offset.Should().Be(TimeSpan.FromHours(1), "19 October is still British Summer Time");
        message.At.Hour.Should().Be(6);
        message.At.Should().Be(SentAt, "it is the same instant");
    }

    [Fact]
    public async Task WithoutADisplayTimeZone_TimesAreShownInUtc()
    {
        (await RenderedMessage("UTC")).At.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AnUnknownDisplayTimeZone_StopsTheSiteStarting()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddWayfinderUmbraco(options => options.DisplayTimeZone = "Mars/Olympus_Mons");

        var read = () => services.BuildServiceProvider().GetRequiredService<IOptions<WayfinderServiceDesignOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*DisplayTimeZone*");
    }
}
