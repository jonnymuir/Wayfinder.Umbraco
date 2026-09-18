using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Configuration;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// REGRESSION: <see cref="ServiceRequestStageService.AdvanceAsync"/> once combined a posted GDS
/// date field's day/month/year boxes with its own unpadded, slash-joined
/// <c>$"{day}/{month}/{year}"</c> — a format that parses back culture-dependently and silently
/// transposed day and month (day=3, month=4 round-tripped as 4 March, not 3 April; day=28,
/// month=7 was rejected outright as an invalid date, because it transposed to month=28).
/// Confirmed live against Umbraco.Prism's "Apply for a juggling licence" demo. Pins the field
/// value the engine actually receives — <see cref="Wayfinder.Rendering.GovUk.GovUk.CombineIsoDate"/>'s
/// unambiguous ISO output, via the shared <c>GovUkStageJourney.CoerceFieldValues</c> this package
/// must reuse rather than re-deriving.
/// </summary>
public sealed class ServiceRequestStageServiceDateFieldTests
{
    private static IFormCollection Form(params (string Key, string Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => new StringValues(e.Value));
        return new FormCollection(dict);
    }

    private static (ServiceRequestStageService Service, Mock<IProcessManager> ProcessManager) BuildService(
        IReadOnlyList<FieldRenderPayload> authoritativeFields)
    {
        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.Advance(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ActorProfile>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns(new ServiceRequestResponseEnvelope
            {
                InstanceId = "instance-1",
                ResponseState = "render",
                StateVersion = 2,
                CorrelationId = "c1",
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Problems = [],
            });

        var nonce = new Mock<IStageNonceService>();
        nonce.Setup(n => n.ResolveAsync("nonce-1", "instance-1", "user-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(authoritativeFields);
        nonce.Setup(n => n.InvalidateAsync("nonce-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new ServiceRequestStageService(
            processManager.Object,
            Options.Create(new WayfinderServiceDesignOptions { ResolveUserId = _ => "user-a" }),
            nonce.Object,
            Mock.Of<Wayfinder.Umbraco.Services.IServiceRequestFileStorage>(),
            Mock.Of<IUploadTokenService>(),
            Mock.Of<IAntiforgery>(),
            NullLogger<ServiceRequestStageService>.Instance);

        return (service, processManager);
    }

    private static IReadOnlyList<FieldRenderPayload> JugglingLicenceApplicantDetailsFields() =>
    [
        new FieldRenderPayload { FieldKey = "full-name", Label = "Full name", FieldType = "text", Required = true },
        new FieldRenderPayload { FieldKey = "email-address", Label = "Email address", FieldType = "email", Required = true },
        new FieldRenderPayload { FieldKey = "date-of-birth", Label = "Date of birth", FieldType = "date", Required = true },
    ];

    [Fact]
    public async Task AdvanceAsync_ADateField_CombinesDayMonthYearAsAnUnambiguousIsoValue_NotTransposed()
    {
        var (service, processManager) = BuildService(JugglingLicenceApplicantDetailsFields());
        var ctx = new DefaultHttpContext();
        var form = Form(
            ("ReturnUrl", "/apply-for-a-juggling-licence"),
            ("InstanceId", "instance-1"),
            ("Nonce", "nonce-1"),
            ("BlueprintKey", "apply-for-a-juggling-licence"),
            ("Action", "continue"),
            ("StateVersion", "1"),
            ("field:full-name", "Test Person"),
            ("field:email-address", "test@example.com"),
            ("field:date-of-birth-day", "3"),
            ("field:date-of-birth-month", "4"),
            ("field:date-of-birth-year", "1990"));

        await service.AdvanceAsync(ctx, form);

        processManager.Verify(p => p.Advance(
            "instance-1", It.IsAny<string>(), "user-a", It.IsAny<ActorProfile>(), "continue", 1,
            It.Is<Dictionary<string, object?>>(fv =>
                fv.ContainsKey("date-of-birth") && Equals(fv["date-of-birth"], "1990-04-03"))),
            Times.Once,
            "day=3, month=4 must reach the engine as 1990-04-03 (3 April), not transposed to 4 March");
    }

    [Fact]
    public async Task AdvanceAsync_ADateField_AcceptsADayGreaterThanTwelve_WhichWouldFalselyLookLikeAnInvalidMonthIfTransposed()
    {
        var (service, processManager) = BuildService(JugglingLicenceApplicantDetailsFields());
        var ctx = new DefaultHttpContext();
        var form = Form(
            ("ReturnUrl", "/apply-for-a-juggling-licence"),
            ("InstanceId", "instance-1"),
            ("Nonce", "nonce-1"),
            ("BlueprintKey", "apply-for-a-juggling-licence"),
            ("Action", "continue"),
            ("StateVersion", "1"),
            ("field:full-name", "Test Person"),
            ("field:email-address", "test@example.com"),
            ("field:date-of-birth-day", "28"),
            ("field:date-of-birth-month", "7"),
            ("field:date-of-birth-year", "1990"));

        var result = await service.AdvanceAsync(ctx, form);

        result.Problems.Should().BeEmpty("28 July 1990 is a genuinely valid date — it must not fail validation");
        processManager.Verify(p => p.Advance(
            "instance-1", It.IsAny<string>(), "user-a", It.IsAny<ActorProfile>(), "continue", 1,
            It.Is<Dictionary<string, object?>>(fv =>
                fv.ContainsKey("date-of-birth") && Equals(fv["date-of-birth"], "1990-07-28"))),
            Times.Once);
    }
}
