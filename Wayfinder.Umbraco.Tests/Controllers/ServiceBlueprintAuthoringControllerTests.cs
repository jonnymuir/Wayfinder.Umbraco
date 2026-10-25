using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Services;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Controllers;

namespace Wayfinder.Umbraco.Tests.Controllers;

/// <summary>The backoffice authoring API carries a whole blueprint as YAML, in both directions.</summary>
public sealed class ServiceBlueprintAuthoringControllerTests
{
    private const string Licence = """
        definitionKey: licence
        displayName: Apply for a licence
        version: 3
        initialStage: details
        queues:
          - key: citizen
            displayName: Citizen
        stages:
          - stageKey: details
            displayName: Your details
            queueKey: citizen
            components:
              - type: text
                fieldKey: email
                label: Email address
        """;

    private sealed class MemoryStore : IServiceBlueprintSourceStore
    {
        public Dictionary<string, ServiceBlueprint> Blueprints { get; } = [];

        public Task<IReadOnlyList<ServiceBlueprintSourceSummary>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ServiceBlueprintSourceSummary>>(
                Blueprints.Values.Select(b => new ServiceBlueprintSourceSummary(b.DefinitionKey, b.DisplayName)).ToArray());

        public Task<ServiceBlueprint?> LoadAsync(string definitionKey, CancellationToken ct = default) =>
            Task.FromResult(Blueprints.GetValueOrDefault(definitionKey));

        public Task<ServiceBlueprintSaveResult> SaveAsync(ServiceBlueprint blueprint, int expectedVersion, CancellationToken ct = default)
        {
            Blueprints[blueprint.DefinitionKey] = blueprint with { Version = expectedVersion + 1 };
            return Task.FromResult(new ServiceBlueprintSaveResult(true, expectedVersion + 1, "memory"));
        }

        public Task<bool> DeleteAsync(string definitionKey, CancellationToken ct = default) =>
            Task.FromResult(Blueprints.Remove(definitionKey));
    }

    private readonly MemoryStore _store = new();

    public ServiceBlueprintAuthoringControllerTests() =>
        _store.Blueprints["licence"] = ServiceBlueprintYaml.Deserialize(Licence);

    private ServiceBlueprintAuthoringController Controller(string? body = null) => new(new ServiceBlueprintAuthoringService(_store))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? "")), ContentType = "application/yaml" },
            },
        },
    };

    [Fact]
    public async Task ReadingABlueprint_ReturnsItsYaml()
    {
        var result = await Controller().ReadServiceBlueprint("licence", CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/yaml");
        ServiceBlueprintYaml.Deserialize(content.Content!).DisplayName.Should().Be("Apply for a licence");
    }

    [Fact]
    public async Task SavingYaml_PersistsTheBlueprint()
    {
        var edited = Licence.Replace("displayName: Apply for a licence", "displayName: Apply for a juggling licence", StringComparison.Ordinal);

        var result = await Controller(edited).SaveServiceBlueprint("licence", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _store.Blueprints["licence"].DisplayName.Should().Be("Apply for a juggling licence");
        _store.Blueprints["licence"].Version.Should().Be(4);
    }

    [Fact]
    public async Task SavingTextThatIsNotABlueprint_IsABadRequest_AndSavesNothing()
    {
        var result = await Controller("definitionKey: licence\nstagez: []\n").SaveServiceBlueprint("licence", CancellationToken.None);

        var outcome = result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ServiceBlueprintSaveOutcome>().Subject;
        outcome.Diagnostics.Should().ContainSingle(d => d.Code == "INVALID_YAML" && d.Message.Contains("Line 2", StringComparison.Ordinal));
        _store.Blueprints["licence"].Version.Should().Be(3);
    }

    [Fact]
    public async Task ValidatingYaml_ReportsWhatASaveWouldReject()
    {
        const string routeStraightToAStage = """
            definitionKey: x
            initialStage: a
            stages:
              - stageKey: a
                routes:
                  - id: a--continue--b
                    target: b
                    trigger: continue
              - stageKey: b
            """;

        var result = await Controller(routeStraightToAStage).ValidateServiceBlueprint(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ServiceBlueprintValidationOutcome>()
            .Which.IsValid.Should().BeFalse();
    }
}
