using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Wayfinder.Engine.Services;
using Wayfinder.Engine.Stores;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Services.Sanitization;
using UmbracoProcessManagerEngine = Wayfinder.Umbraco.Services.UmbracoProcessManagerEngine;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// The reference app's njf-coaching-register blueprint pre-fills the applicant's name and email
/// from the signed-in user purely declaratively (a <c>user</c> service field and
/// <c>defaultFrom</c>), with no host code. These run the real seeded blueprint through the real
/// Umbraco engine.
/// </summary>
public sealed class UserServiceValueDefaultingTests
{
    private const string TenantId = "tenant";

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set => throw new NotSupportedException(); }
    }

    private static string? RenderedValue(ClaimsPrincipal user, string fieldKey)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "njf-coaching-register.json"));
        var definition = JsonSerializer.Deserialize<ServiceBlueprint>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowOutOfOrderMetadataProperties = true })!;

        var engine = new UmbracoProcessManagerEngine(
            NullLogger<UmbracoProcessManagerEngine>.Instance,
            new SingleDefinitionServiceBlueprintStore(definition),
            new PassthroughContentSanitizer(),
            new InMemoryServiceRequestStore(),
            new FixedHttpContextAccessor(new DefaultHttpContext { User = user }));

        var result = engine.GetCurrent(definition.DefinitionKey, TenantId, "user-1", new ActorProfile());

        return result.Render!.Components
            .SelectMany(c => c.Fields)
            .Single(f => f.FieldKey == fieldKey)
            .Value?.ToString();
    }

    private static ClaimsPrincipal Alex() => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Alex Applicant"),
            new Claim(ClaimTypes.Email, "alex@example.test"),
        ],
        authenticationType: "test"));

    [Fact]
    public void GivenASignedInApplicant_WhenTheApplyStageRenders_ThenNameIsPrefilled() =>
        RenderedValue(Alex(), "applicantName").Should().Be("Alex Applicant");

    [Fact]
    public void GivenASignedInApplicant_WhenTheApplyStageRenders_ThenEmailIsPrefilled() =>
        RenderedValue(Alex(), "applicantEmail").Should().Be("alex@example.test");

    [Fact]
    public void GivenAnAnonymousVisitor_WhenTheApplyStageRenders_ThenTheFieldsAreEmpty()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        (RenderedValue(anonymous, "applicantName") ?? "").Should().BeEmpty();
        (RenderedValue(anonymous, "applicantEmail") ?? "").Should().BeEmpty();
    }
}
