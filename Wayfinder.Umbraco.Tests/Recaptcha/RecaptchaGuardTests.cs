using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Wayfinder.Umbraco.Recaptcha;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Recaptcha;

/// <summary>
/// SECURITY REGRESSION (bot protection). The reCAPTCHA guard fails closed: a submission goes through only
/// when Google confirms the token, for this action, with at least the minimum score. No token, no secret,
/// a low score, another action's token, or Google failing to answer all turn it away.
/// </summary>
public sealed class RecaptchaGuardTests
{
    private sealed class Google(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static (RecaptchaSubmissionGuard Guard, Google Google) Build(
        string json = """{"success":true,"score":0.9,"action":"contact"}""",
        HttpStatusCode status = HttpStatusCode.OK,
        string secret = "secret-key",
        Func<HttpContext, bool>? skip = null)
    {
        var google = new Google(status, json);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(google));
        var versions = new Mock<IFileVersionProvider>();
        versions.Setup(v => v.AddFileVersionToPath(It.IsAny<PathString>(), It.IsAny<string>())).Returns<PathString, string>((_, p) => p + "?v=1");
        var options = new WayfinderRecaptchaOptions { SiteKey = "site\"><script>", SecretKey = secret, Action = "contact", Skip = skip ?? (_ => false) };
        return (new RecaptchaSubmissionGuard(Options.Create(options), clients.Object, versions.Object, NullLogger<RecaptchaSubmissionGuard>.Instance), google);
    }

    private static Task<string?> Check(RecaptchaSubmissionGuard guard, string? token = "token-1") =>
        guard.CheckAsync(
            new StageSubmissionGuardContext(new DefaultHttpContext(), "contact", "instance-1"),
            new FormCollection(token is null ? [] : new Dictionary<string, StringValues> { [RecaptchaSubmissionGuard.TokenField] = token }));

    [Fact]
    public async Task AConfirmedHumanSubmission_GoesThrough_AndTheSecretTravelsInTheBodyNotTheAddress()
    {
        var (guard, google) = Build();

        (await Check(guard)).Should().BeNull();
        google.LastUri!.Query.Should().BeEmpty();
        google.LastBody.Should().Contain("secret=secret-key").And.Contain("response=token-1");
    }

    [Theory]
    [InlineData("""{"success":true,"score":0.1,"action":"contact"}""")]
    [InlineData("""{"success":true,"score":0.9,"action":"login"}""")]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""")]
    [InlineData("not json")]
    public async Task AnUnconfirmedSubmission_IsTurnedAway(string googleSays)
    {
        (await Check(Build(googleSays).Guard)).Should().NotBeNull();
    }

    [Fact]
    public async Task GoogleFailingToAnswer_TurnsTheSubmissionAway()
    {
        (await Check(Build(status: HttpStatusCode.InternalServerError).Guard)).Should().NotBeNull();
    }

    [Fact]
    public async Task NoToken_IsTurnedAway_WithoutAskingGoogle()
    {
        var (guard, google) = Build();

        (await Check(guard, token: null)).Should().NotBeNull();
        google.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NoSecretKey_TurnsEverySubmissionAway()
    {
        (await Check(Build(secret: "").Guard)).Should().NotBeNull();
    }

    [Fact]
    public async Task SomeoneTheSiteSkips_IsNeitherAskedNorChecked()
    {
        var (guard, google) = Build(skip: _ => true);

        (await Check(guard, token: null)).Should().BeNull();
        guard.FormFields(new StageSubmissionGuardContext(new DefaultHttpContext(), "contact", "instance-1")).Should().BeNull();
        google.Calls.Should().Be(0);
    }

    [Fact]
    public void TheFormsReCaptchaMarkup_EncodesTheConfiguredValues()
    {
        var (guard, _) = Build();
        using var writer = new StringWriter();

        guard.FormFields(new StageSubmissionGuardContext(new DefaultHttpContext(), "contact", "instance-1"))!
            .WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);

        writer.ToString().Should().NotContain("<script>\"").And.Contain("site&quot;&gt;&lt;script&gt;");
        writer.ToString().Should().Contain(RecaptchaSubmissionGuard.ScriptPath + "?v=1");
    }
}
