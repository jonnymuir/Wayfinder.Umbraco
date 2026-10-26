using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Starter;

namespace Wayfinder.Umbraco.Tests.Starter;

/// <summary>
/// SECURITY REGRESSION. On a starter site the public and the demo caseworker are kept apart by the
/// blueprints alone: the public starts and acts only in each service's first queue and only on their
/// own requests, the caseworker works every other queue, and nobody reaches anything when there are no
/// services. A blueprint designed later, with queue names nobody configured, is covered the same way.
/// </summary>
public class StarterAccessTests
{
    private static readonly ServiceBlueprint SampleService = LoadSample();

    private static readonly ServiceBlueprint DesignedLater = ServiceBlueprintYaml.Deserialize("""
        definitionKey: report-a-streetlight
        displayName: Report a broken streetlight
        version: 1
        initialStage: where
        queues:
          - key: resident
            displayName: Resident
            actor: resident
          - key: highways
            displayName: Highways team
            actor: highways-officer
            assignmentPolicy: team-tray
            owningTeamId: highways-team
            roleGates: [highways]
        stages:
          - stageKey: where
            displayName: Where is the streetlight?
            queueKey: resident
          - stageKey: decide
            displayName: Decide
            queueKey: highways
        """);

    [Fact]
    public void AMemberOfThePublic_StartsAndActsOnlyInEachServicesFirstQueue_OnTheirOwnRequests()
    {
        var profile = StarterAccess.Resolve(Anonymous(), [SampleService, DesignedLater]);

        profile.CanStartQueue("caller").Should().BeTrue();
        profile.CanStartQueue("resident").Should().BeTrue();
        profile.CanActInQueue("contact-centre").Should().BeFalse();
        profile.CanViewQueue("highways").Should().BeFalse();
        profile.CanStartQueue("highways").Should().BeFalse();
        profile.RestrictToInstanceOwner.Should().BeTrue();
    }

    [Fact]
    public void TheCaseworker_WorksEveryOtherQueue_AndNeverAPublicOne()
    {
        var profile = StarterAccess.Resolve(StarterPersonas.Caseworker(), [SampleService, DesignedLater]);

        profile.CanActInQueue("contact-centre").Should().BeTrue();
        profile.CanActInQueue("highways").Should().BeTrue();
        profile.CanViewQueue("caller").Should().BeFalse();
        profile.CanStartQueue("resident").Should().BeFalse();
        profile.CanActInQueue("resident").Should().BeFalse();
        profile.RestrictToInstanceOwner.Should().BeFalse();
    }

    [Fact]
    public void TheCaseworker_BelongsToTheTeamsAndHoldsTheRolesTheStaffQueuesAskFor()
    {
        var profile = StarterAccess.Resolve(StarterPersonas.Caseworker(), [DesignedLater]);

        profile.IsTeamMember("highways-team").Should().BeTrue();
        profile.HasCapability(["highways"]).Should().BeTrue();
    }

    [Fact]
    public void WithNoServicesAtAll_NobodyReachesAnyQueue()
    {
        foreach (var user in new[] { Anonymous(), StarterPersonas.Caseworker() })
        {
            var profile = StarterAccess.Resolve(user, []);

            profile.CanStartQueue("anything").Should().BeFalse();
            profile.CanViewQueue("anything").Should().BeFalse();
            profile.CanActInQueue("anything").Should().BeFalse();
        }
    }

    [Fact]
    public void ACaseworkerRoleFromAnotherSignIn_IsNotTheStarterCaseworker()
    {
        var elsewhere = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, StarterPersonas.CaseworkerRole)], "SomeOtherScheme"));

        var profile = StarterAccess.Resolve(elsewhere, [SampleService]);

        profile.CanActInQueue("contact-centre").Should().BeFalse();
        profile.RestrictToInstanceOwner.Should().BeTrue();
    }

    [Fact]
    public void TheSampleService_StartsInItsPublicQueue()
    {
        StarterAccess.PublicQueueKey(SampleService).Should().Be("caller");
    }

    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("Production", false, false)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", true, true)]
    public void TheStarter_RunsOnlyInDevelopment_UnlessADemoServerSaysOtherwise(string environmentName, bool allowOutsideDevelopment, bool mayRun)
    {
        var environment = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environmentName);

        StarterOptions.MayRun(new StarterOptions { AllowOutsideDevelopment = allowOutsideDevelopment }, environment)
            .Should().Be(mayRun);
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ServiceBlueprint LoadSample()
    {
        using var stream = typeof(StarterSeeder).Assembly.GetManifestResourceStream($"{StarterSeeder.SampleBlueprintKey}.yaml")!;
        using var reader = new StreamReader(stream);
        return ServiceBlueprintYaml.Deserialize(reader.ReadToEnd());
    }
}
