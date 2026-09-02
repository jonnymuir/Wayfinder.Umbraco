using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Engine.Extensions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Models.ServiceDesign.SupportSystems;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// The reference app's <c>njf-coaching-register</c> blueprint and its
/// <c>Wayfinder:SupportSystems</c> configuration are maintained in two separate committed files.
/// Nothing in the compiler ties an input key, an output key, or an outcome key in one to the
/// other. These tests bind the real committed <c>appsettings.json</c> section through
/// <see cref="SupportSystemServiceCollectionExtensions.AddConfiguredSupportSystems"/> and validate
/// the real committed blueprint against the registry it produces, so a rename in one file that is
/// not mirrored in the other goes red here rather than only in a live journey.
/// </summary>
public sealed class ConfiguredSupportSystemBlueprintTests : IClassFixture<ConfiguredSupportSystemBlueprintTests.RegistryFixture>
{
    /// <summary>
    /// Registers the reference app's configured support systems into the process-wide
    /// <see cref="SupportSystemRegistry"/> exactly once. The registry has no public reset (that is
    /// deliberate — see its <c>ResetForTests</c> remarks), so registration must be one-shot and
    /// every test in this class reads the same frozen result.
    /// </summary>
    public sealed class RegistryFixture
    {
        public RegistryFixture()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(FixturesDir, "referenceapp.appsettings.json"))
                .Build();
            new ServiceCollection().AddLogging().AddConfiguredSupportSystems(config);
        }
    }

    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static ServiceBlueprint CoachingRegisterBlueprint()
    {
        var json = File.ReadAllText(Path.Combine(FixturesDir, "njf-coaching-register.json"));
        return JsonSerializer.Deserialize<ServiceBlueprint>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowOutOfOrderMetadataProperties = true })!;
    }

    [Fact]
    public void TheReferenceApp_RegistersTheNjfCoachingStandardsSupportSystem_FromConfigurationAlone()
    {
        var capability = SupportSystemRegistry.FindCapability("njf-coaching-standards", "check-coaching-standards");

        capability.Should().NotBeNull();
        capability!.Inputs.Select(i => i.Key).Should()
            .BeEquivalentTo("applicantName", "yearsCoaching", "disclosureReference", "firstAidExpiry");
        capability.Outputs.Select(o => o.Key).Should()
            .BeEquivalentTo("coachingStandardsOutcome", "coachingStandardsNote");
        capability.Outcomes.Select(o => o.Key).Should()
            .BeEquivalentTo("accredited", "provisional", "referred");
        capability.SupportedCompletionModes.Should().Equal(SupportSystemCompletionMode.Webhook);
    }

    [Fact]
    public void TheCoachingRegisterBlueprint_PassesSupportSystemValidation_AgainstThatRegistration()
    {
        var errors = CoachingRegisterBlueprint().ValidateSupportSystemActions()
            .Where(d => d.Severity == ServiceBlueprintDiagnosticSeverity.Error)
            .ToArray();

        errors.Should().BeEmpty(
            "the support-system-call action's inputs and the calling stage's outgoing route triggers " +
            "must all match the configured capability — " +
            string.Join("; ", errors.Select(e => $"{e.Code} at {e.Path}: {e.Message}")));
    }

    [Fact]
    public void TheCoachingRegisterBlueprint_CanDisplayTheSupportSystemOutputs_WithoutTrippingDataDisplayValidation()
    {
        var unknownFieldErrors = CoachingRegisterBlueprint().ValidateDataDisplayBindings()
            .Where(d => d.Severity == ServiceBlueprintDiagnosticSeverity.Error)
            .ToArray();

        unknownFieldErrors.Should().BeEmpty(
            "coachingStandardsOutcome/coachingStandardsNote are declared capability Outputs, so a " +
            "summary-list may bind to them even though no stage captures them as input — " +
            string.Join("; ", unknownFieldErrors.Select(e => $"{e.Code} at {e.Path}: {e.Message}")));
    }
}
