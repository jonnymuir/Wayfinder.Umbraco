using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Automations;
using Umbraco.Automate.Core.Bindings;
using Umbraco.Automate.Core.Conditions;
using Wayfinder.Umbraco.ReferenceApp;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// The "NJF Coaching Standards" automation the reference app seeds, read back from Automate's own
/// store and evaluated with Automate's own condition and binding evaluators, so these hold for what
/// a run actually does rather than for how the seeder happens to build it.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class CoachingStandardsAutomationTests(ReferenceAppFactory factory)
{
    // Automate reads step settings case-insensitively with operator names (its own JsonOptions.Settings).
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task TheFastTrackCondition_OpensInAutomatesConditionBuilderWithItsConditionsShowing()
    {
        var conditions = JsonNode.Parse(Json(Step(await SeededAutomation(), "umbracoAutomate.if").Settings["conditions"]))!;

        // The backoffice condition builder only reads PascalCase "Groups"/"Conditions" with
        // operator names; anything else opens as an empty builder.
        var group = conditions["Groups"]!.AsArray().Should().ContainSingle().Subject!;
        group["Conditions"]!.AsArray().Select(c => c!["Operator"]!.GetValue<string>())
            .Should().Equal("GreaterThanOrEquals", "IsNotEmpty");
    }

    [Theory]
    [InlineData(5, "DBS-001", true)]
    [InlineData(2, "DBS-001", true)]
    [InlineData(1, "DBS-001", false)]
    [InlineData(5, "", false)]
    public async Task AnApplication_FastTracksOnlyWithTwoOrMoreYearsAndADisclosureReference(
        int yearsCoaching, string disclosureReference, bool fastTracks)
    {
        var settings = Step(await SeededAutomation(), "umbracoAutomate.if").Settings["conditions"];
        var conditions = JsonSerializer.Deserialize<ConditionSet>(Json(settings), SettingsJson)!;

        var run = Bindings(inputs: new() { ["yearsCoaching"] = yearsCoaching, ["disclosureReference"] = disclosureReference });

        factory.Services.GetRequiredService<ConditionEvaluator>().Evaluate(conditions, run).Should().Be(fastTracks);
    }

    [Theory]
    [InlineData("provisional")]
    [InlineData("referred")]
    public async Task TheStandardsOfficersComment_ReachesTheNoteExactlyAsTyped(string outcome)
    {
        const string comment = "Lovely \"three-ball cascade\".\nMentor: Sam, by 1 March.";

        var note = ResolvedNote(await SeededAutomation(), outcome, comment);

        note.Should().Be(comment);
    }

    [Theory]
    [InlineData("provisional", null)]
    [InlineData("referred", "")]
    public async Task ABlankComment_FallsBackToTheStandardNoteForThatOutcome(string outcome, string? comment)
    {
        var note = ResolvedNote(await SeededAutomation(), outcome, comment);

        note.Should().NotBeNullOrWhiteSpace();
        note.Should().ContainEquivalentOf(outcome == "provisional" ? "provisionally accredited" : "referred");
    }

    // A run exposes each step's output under its alias, so the officer's decision is keyed by the
    // approval step's own stored alias, proving the resolve step reads that step.
    private string? ResolvedNote(Automation automation, string outcome, string? comment)
    {
        var approval = Step(automation, "umbracoAutomate.requestApproval");
        var run = Bindings(decision: (approval.Alias!, new() { ["comment"] = comment }));
        var payload = factory.Services.GetRequiredService<BindingEvaluator>()
            .Evaluate(Text(Resolve(automation, outcome).Settings["resultPayload"]), run);

        return JsonNode.Parse(payload)!["coachingStandardsNote"]?.GetValue<string>();
    }

    private static Dictionary<string, object?> Bindings(
        Dictionary<string, object?>? inputs = null, (string Alias, Dictionary<string, object?> Output)? decision = null) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["trigger"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["body"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["invocationId"] = "invocation-1",
                    ["inputs"] = inputs ?? [],
                },
            },
            ["steps"] = decision is { } d
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [d.Alias] = d.Output }
                : new Dictionary<string, object?>(),
        };

    private async Task<Automation> SeededAutomation()
    {
        var automations = factory.Services.GetRequiredService<IAutomationService>();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            if (await automations.GetAutomationAsync(AutomateCoachingStandardsSeeder.AutomationId) is { Steps.Count: > 0 } automation)
            {
                return automation;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("The reference app never seeded the NJF Coaching Standards automation.");
    }

    private static StepConfiguration Step(Automation automation, string actionAlias) =>
        automation.Steps.Should().ContainSingle(s => s.ActionAlias == actionAlias).Subject;

    private static StepConfiguration Resolve(Automation automation, string outcome) =>
        automation.Steps.Should().ContainSingle(s =>
            s.ActionAlias == "wayfinder.resolveSupportSystemOutcome" && Text(s.Settings["outcomeKey"]) == outcome).Subject;

    private static string Json(object? value) => value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value);

    private static string Text(object? value) => value is JsonElement { ValueKind: JsonValueKind.String } element
        ? element.GetString()!
        : value?.ToString() ?? "";
}
