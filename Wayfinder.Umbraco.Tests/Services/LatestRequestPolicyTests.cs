using FluentAssertions;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// SECURITY REGRESSION (cross-person disclosure). The latest-request policy hands back the caller's own
/// most recent request, never anyone else's, however recent theirs is.
/// </summary>
public sealed class LatestRequestPolicyTests
{
    private static readonly ServiceBlueprint Contact = new() { DefinitionKey = "contact", DisplayName = "Contact us", InitialStage = "message" };

    private static ServiceRequest Request(string id, string userId, int minutesAgo, bool aborted = false) => new()
    {
        InstanceId = id, BlueprintKey = "contact", TenantId = "default", UserId = userId, CurrentStage = "message",
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), IsAborted = aborted,
    };

    private static Task<RequestConcurrencyDecision> Decide(string userId, params ServiceRequest[] candidates) =>
        new LatestRequestPolicy("contact").EvaluateAsync(Contact, "default", userId, new ActorProfile(), candidates);

    [Fact]
    public async Task TheCallersMostRecentRequest_IsPickedUp()
    {
        var decision = await Decide("visitor:a", Request("old", "visitor:a", 60), Request("recent", "visitor:a", 5));

        decision.Outcome.Should().Be(RequestConcurrencyOutcome.ReuseExisting);
        decision.ExistingInstance!.InstanceId.Should().Be("recent");
    }

    [Fact]
    public async Task SomeoneElsesRequest_IsNeverPickedUp_HoweverRecent()
    {
        var decision = await Decide("visitor:a", Request("mine", "visitor:a", 60), Request("theirs", "visitor:b", 1));

        decision.ExistingInstance!.InstanceId.Should().Be("mine");
    }

    [Fact]
    public async Task WithNoRequestOfTheirOwn_ANewOneStarts()
    {
        var decision = await Decide("visitor:a", Request("theirs", "visitor:b", 1), Request("stopped", "visitor:a", 1, aborted: true));

        decision.Outcome.Should().Be(RequestConcurrencyOutcome.AllowNew);
    }
}
