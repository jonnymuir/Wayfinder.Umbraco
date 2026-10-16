using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Supplies values for a blueprint's <c>source: "service"</c> calculation fields that the host
/// looks up, such as a member record from a system of record. Register one in DI and the engine
/// uses it; it is constructed by DI like any other service, so it can take the clients it needs.
/// <para/>
/// Only supply what you look up. A declared service field you leave out is read from the
/// request's stored values (where the engine's own actions write theirs), and the built-in
/// <c>user</c> value is added unless you supply <c>user</c> yourself. Called on every render and
/// advance, possibly with no current HTTP request (a support-system callback resuming a request),
/// so don't depend on one being there.
/// </summary>
public interface IServiceInputsResolver
{
    IReadOnlyDictionary<string, object?>? Resolve(ServiceRequest request, ServiceBlueprint blueprint, StageDefinition stage);
}
