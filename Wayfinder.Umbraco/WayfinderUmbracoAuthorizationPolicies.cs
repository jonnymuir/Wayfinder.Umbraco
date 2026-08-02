namespace Wayfinder.Umbraco;

/// <summary>
/// Named authorization policies Wayfinder.Umbraco's own controllers reference by name, rather
/// than hardcoding an authentication scheme — a host registers each policy to mean whatever
/// scheme (or requirement) makes sense for it. Keeps this package free of any opinion about
/// how a host authenticates its members.
/// </summary>
public static class WayfinderUmbracoAuthorizationPolicies
{
    /// <summary>
    /// Required to call <see cref="Controllers.ServiceRequestPollController"/>'s polling
    /// endpoint. A host must register this policy (e.g. requiring its own member cookie
    /// scheme) — see the host's own composition for how Prism's "PrismMemberCookie" scheme
    /// is wired to it.
    /// </summary>
    public const string ServiceRequestPolling = "Wayfinder:ServiceRequestPolling";

    /// <summary>
    /// Required to call <see cref="Controllers.ServiceBlueprintAuthoringController"/>. Unlike
    /// <see cref="ServiceRequestPolling"/>, this policy is registered by Wayfinder.Umbraco's own
    /// composer (<see cref="WayfinderUmbracoComposer"/>) — backoffice group membership is
    /// something this package already has full information about via
    /// <c>IBackOfficeSecurityAccessor</c>, so a host needs no wiring for it. Membership is
    /// checked against <see cref="Configuration.WayfinderServiceDesignOptions.AdminGroupAliases"/>
    /// — the same list <see cref="WayfinderSectionAccessSeeder"/> grants the "Blueprints" section
    /// to, so backoffice nav visibility and API enforcement can't silently drift apart.
    /// </summary>
    public const string BlueprintsAdmin = "Wayfinder:BlueprintsAdmin";
}
