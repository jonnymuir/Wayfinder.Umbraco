using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using uSync.Core;
using uSync.Core.Models;
using uSync.Core.Serialization;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Services;
using Wayfinder.Umbraco.Persistence;
using Wayfinder.Umbraco.Serialization;

namespace Wayfinder.Umbraco.Tests.Serialization;

/// <summary>
/// SECURITY REGRESSION: uSync import used to skip <see cref="ServiceBlueprintAuthoringService.Validate"/>
/// entirely and, on a malformed stored JSON payload, throw out of <c>SaveItemAsync</c> after the
/// row had already been written. <see cref="ServiceBlueprintSerializer.DeserializeCoreAsync"/> now
/// parses and validates before touching the store at all.
///
/// These tests call the protected <c>DeserializeCoreAsync</c> directly (via a test-only subclass
/// exposing it) rather than uSync's public <c>DeserializeAsync</c>: that base-class method calls
/// its own <c>IsCurrentAsync</c>/<c>FindItemAsync</c> pipeline first — real Umbraco-database
/// behaviour this class doesn't own and isn't part of this fix — before ever reaching the
/// override under test here.
/// </summary>
public sealed class ServiceBlueprintSerializerTests
{
    [SyncSerializer("7a2e4f18-9c3b-4d67-a1e5-8f6b2c9d4a71", "Wayfinder Service Blueprint Serializer (test)", "wayfinderServiceBlueprint")]
    private sealed class TestableSerializer(
        Microsoft.Extensions.Logging.ILogger<SyncSerializerRoot<ServiceBlueprintSchema>> logger,
        IUmbracoDatabaseFactory databaseFactory,
        IProcessManager engine,
        ServiceBlueprintAuthoringService authoringService)
        : ServiceBlueprintSerializer(logger, databaseFactory, engine, authoringService)
    {
        public Task<SyncAttempt<ServiceBlueprintSchema>> DeserializeForTestAsync(
            System.Xml.Linq.XElement node, SyncSerializerOptions options) =>
            DeserializeCoreAsync(node, options);
    }

    private const string ValidBlueprintJson = """
        {
          "definitionKey": "import-test",
          "displayName": "Import test",
          "version": 1,
          "initialStage": "start",
          "requestPolicy": "multiple",
          "queues": [ { "key": "citizen", "displayName": "Citizen", "actor": "citizen" } ],
          "stages": [
            {
              "stageKey": "start",
              "displayName": "Start",
              "queueKey": "citizen",
              "components": [ { "type": "panel", "heading": "Start" } ],
              "routes": []
            }
          ]
        }
        """;

    private static TestableSerializer BuildSerializer() => new(
        NullLogger<SyncSerializerRoot<ServiceBlueprintSchema>>.Instance,
        Mock.Of<IUmbracoDatabaseFactory>(),
        Mock.Of<IProcessManager>(),
        new ServiceBlueprintAuthoringService(Mock.Of<IServiceBlueprintSourceStore>()));

    /// <summary>Builds a real, well-formed uSync node via the serializer's own SerializeAsync,
    /// then swaps in whatever Definition JSON the test wants to attempt to import — guarantees the
    /// node shape (root element, Key/Alias attributes) matches exactly what a real export
    /// produces, rather than a hand-built approximation of uSync's internal node format.</summary>
    private static async Task<System.Xml.Linq.XElement> BuildNodeWithDefinitionAsync(
        TestableSerializer serializer, string definitionJson)
    {
        var schema = new ServiceBlueprintSchema
        {
            DefinitionKey = "import-test",
            DisplayName = "Import test",
            Version = 1,
            Json = ValidBlueprintJson,
        };
        var serialized = await serializer.SerializeAsync(schema, new SyncSerializerOptions());
        serialized.Success.Should().BeTrue("the fixture export itself must succeed for these tests to mean anything");

        var node = serialized.Item!;
        node.Element("Definition")!.ReplaceWith(new System.Xml.Linq.XElement("Definition", new System.Xml.Linq.XCData(definitionJson)));
        return node;
    }

    [Fact]
    public async Task DeserializeAsync_FailsCleanly_ForMalformedJson_InsteadOfThrowingAfterAWrite()
    {
        var serializer = BuildSerializer();
        var node = await BuildNodeWithDefinitionAsync(serializer, "{ this is not valid json");

        var result = await serializer.DeserializeForTestAsync(node, new SyncSerializerOptions());

        result.Success.Should().BeFalse("malformed stored JSON must fail the sync attempt, not throw");
        result.Change.Should().Be(ChangeType.Fail);
    }

    [Fact]
    public async Task DeserializeAsync_FailsTheSyncAttempt_ForAStructurallyInvalidBlueprint()
    {
        // Stage routes must always target a gateway, never another stage directly
        // (ValidateGatewayRouting) — "start" here routes straight to "end", which isn't declared
        // as a gateway, exactly the shape a real backoffice save would reject.
        const string invalidBlueprintJson = """
            {
              "definitionKey": "import-test",
              "displayName": "Import test",
              "version": 1,
              "initialStage": "start",
              "requestPolicy": "multiple",
              "queues": [ { "key": "citizen", "displayName": "Citizen", "actor": "citizen" } ],
              "stages": [
                {
                  "stageKey": "start",
                  "displayName": "Start",
                  "queueKey": "citizen",
                  "components": [ { "type": "panel", "heading": "Start" } ],
                  "routes": [ { "id": "start--submit--end", "target": "end", "trigger": "submit" } ]
                },
                {
                  "stageKey": "end",
                  "displayName": "End",
                  "queueKey": "citizen",
                  "components": [ { "type": "panel", "heading": "End" } ],
                  "routes": []
                }
              ]
            }
            """;

        var serializer = BuildSerializer();
        var node = await BuildNodeWithDefinitionAsync(serializer, invalidBlueprintJson);

        var result = await serializer.DeserializeForTestAsync(node, new SyncSerializerOptions());

        result.Success.Should().BeFalse(
            "an import naming a nonexistent initial stage must fail validation exactly like the REST authoring path does, not be persisted and pushed live");
        result.Change.Should().Be(ChangeType.Fail);
    }
}
