using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Stores;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Extensions;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

/// <summary>
/// A host customises the engine by registering services next to <c>AddWayfinderUmbraco</c>, never
/// by constructing the engine. Each extension point the base engine has must reach it that way:
/// before, the audit log store and concurrency policies were dropped, and a service-inputs
/// resolver could only be passed by building the whole engine by hand.
/// </summary>
public class EngineExtensionPointTests
{
    private const string TenantId = "tenant";
    private const string UserId = "user";
    private const string BlueprintKey = "extension-points";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string BlueprintJson = """
        {
          "definitionKey": "extension-points",
          "displayName": "Extension points",
          "version": 1,
          "initialStage": "start",
          "requestPolicy": "single",
          "calculations": {
            "fields": {
              "rate": { "source": "service" },
              "doubled": { "expr": "rate * 2" }
            }
          },
          "queues": [ { "key": "citizen", "displayName": "Applicant", "actor": "citizen" } ],
          "stages": [
            {
              "stageKey": "start",
              "displayName": "Start",
              "queueKey": "citizen",
              "components": [ { "type": "panel", "heading": "Start" } ],
              "routes": [ { "id": "start--submit--to-done", "target": "to-done", "trigger": "submit" } ]
            },
            { "stageKey": "done", "displayName": "Done", "queueKey": "citizen", "components": [ { "type": "panel", "heading": "Done" } ] }
          ],
          "gateways": [
            {
              "key": "to-done",
              "displayName": "Submit",
              "gatewayType": "Split",
              "queueKey": "citizen",
              "routes": [ { "id": "to-done--continue--done", "target": "done", "trigger": "continue" } ]
            }
          ]
        }
        """;

    [Fact]
    public void ARegisteredAuditLogStore_RecordsTheRequestsEvents()
    {
        var audit = new InMemoryAuditLogStore();
        var engine = EngineWith(services => services.AddSingleton<IAuditLogStore>(audit));

        var started = engine.GetCurrent(BlueprintKey, TenantId, UserId, ActorProfile.UnrestrictedOwner);
        engine.Advance(started.InstanceId, TenantId, UserId, ActorProfile.UnrestrictedOwner, "submit", started.StateVersion, null);

        audit.GetByInstance(started.InstanceId).Should().Contain(e => e.EventType == AuditEventType.Transition && e.Actor == UserId);
    }

    [Fact]
    public void ARegisteredConcurrencyPolicy_DecidesWhetherARequestStarts()
    {
        var engine = EngineWith(services => services.AddSingleton<IRequestConcurrencyPolicy>(
            new FixedPolicy(BlueprintKey, RequestConcurrencyDecision.Deny("Closed for the month-end run."))));

        var result = engine.GetCurrent(BlueprintKey, TenantId, UserId, ActorProfile.UnrestrictedOwner);

        result.ResponseState.Should().Be("error");
        result.Problems.Should().ContainSingle(p => p.Code == "CONCURRENCY_POLICY_DENIED");
    }

    [Fact]
    public void ARegisteredServiceInputsResolver_SuppliesServiceFields()
    {
        var engine = EngineWith(services => services.AddSingleton<IServiceInputsResolver>(new FixedRate(21m)));

        var started = engine.GetCurrent(BlueprintKey, TenantId, UserId, ActorProfile.UnrestrictedOwner);

        engine.GetLastCalculationResult(started.InstanceId)!.Fields["doubled"].Should().Be(42m);
    }

    // Registers what a host would, with in-memory stores in place of the database-backed ones
    // (AddWayfinderUmbraco's TryAdd keeps whatever is already registered), then resolves the engine.
    private static UmbracoProcessManagerEngine EngineWith(Action<IServiceCollection> hostRegistrations)
    {
        var definition = JsonSerializer.Deserialize<ServiceBlueprint>(BlueprintJson, JsonOptions)!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        // Umbraco itself registers this on every site.
        services.AddSingleton(Moq.Mock.Of<global::Umbraco.Cms.Core.Events.IEventAggregator>());
        services.AddSingleton<IServiceBlueprintStore>(new SingleDefinitionServiceBlueprintStore(definition));
        services.AddSingleton<IServiceRequestStore>(new InMemoryServiceRequestStore());
        services.AddSingleton<IBulkDatasetStore>(new InMemoryBulkDatasetStore(new InMemoryServiceRequestFileStorage()));
        hostRegistrations(services);
        services.TryAddSingleton<IAuditLogStore>(new InMemoryAuditLogStore());
        services.AddWayfinderUmbraco();

        return services.BuildServiceProvider().GetRequiredService<UmbracoProcessManagerEngine>();
    }

    private sealed class FixedPolicy(string definitionKey, RequestConcurrencyDecision decision) : IRequestConcurrencyPolicy
    {
        public IReadOnlyList<string> DefinitionKeys { get; } = [definitionKey];

        public Task<RequestConcurrencyDecision> EvaluateAsync(
            ServiceBlueprint definition, string tenantId, string userId, ActorProfile accessProfile,
            IReadOnlyList<ServiceRequest> candidateInstances, CancellationToken ct = default) =>
            Task.FromResult(decision);
    }

    private sealed class FixedRate(decimal rate) : IServiceInputsResolver
    {
        public IReadOnlyDictionary<string, object?>? Resolve(ServiceRequest request, ServiceBlueprint blueprint, StageDefinition stage) =>
            new Dictionary<string, object?> { ["rate"] = rate };
    }
}
