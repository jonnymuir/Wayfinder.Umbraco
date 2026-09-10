using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Web.Website.Controllers;
using Wayfinder.Umbraco.Controllers;

namespace Wayfinder.Umbraco.Tests.Controllers;

/// <summary>
/// SECURITY REGRESSION — the authorization contract for every HTTP endpoint this package ships.
///
/// CLAUDE.md §Security 3: "Deny by default. Every endpoint carries an explicit policy
/// (RequireAuthorization); AllowAnonymous only with a written reason in the code."
///
/// This test discovers <em>every</em> controller in the Wayfinder.Umbraco assembly by reflection
/// (not a hand-maintained list), so a newly added controller cannot slip through unattributed.
/// For each action it requires one of:
///   * an [Authorize] (on the class or the method, or inherited), or
///   * an [AllowAnonymous] whose (controller, action-or-"*") key is on <see cref="KnownAnonymous"/>
///     with a one-line reason.
/// A missing decision — neither attribute, or an imperative "if (!User.Identity.IsAuthenticated)"
/// check standing in for one — fails the test.
///
/// The minimal-API endpoint groups this package maps (MCP OAuth discovery) are covered by
/// <see cref="Mcp.WayfinderMcpDiscoveryEndpointsTests"/>, which asserts each is AllowAnonymous
/// (RFC 9728 / RFC 8414 discovery documents must be publicly reachable).
/// </summary>
public class AuthorizationContractTests
{
    /// <summary>
    /// Controllers whose authorization decision is a deliberate imperative check in the action
    /// body rather than an [Authorize] attribute, because the attribute cannot express what they
    /// need. Each still fails closed for an unauthenticated caller — that behaviour is asserted by
    /// a booted-host test (Layer 2), which is what actually verifies the check is still there.
    /// </summary>
    private static readonly Dictionary<Type, string> KnownImperativeAuth = new()
    {
        [typeof(ServiceRequestHubController)] =
            "Index() redirects an unauthenticated caller to WayfinderServiceDesignOptions.LoginPath " +
            "(host-configurable, with a ReturnUrl) — ASP.NET's [Authorize] challenge cannot honour a " +
            "package-level configurable login path. Fail-closed redirect is asserted by " +
            "ServiceRequestHubAuthorizationTests (booted host).",
    };

    /// <summary>
    /// The only endpoints allowed to be anonymous, each with the reason it is safe. A "*" action
    /// means the whole controller. Adding a row here is a deliberate, reviewed act — keep the
    /// list short and the reasons real.
    /// </summary>
    private static readonly Dictionary<(Type Controller, string Action), string> KnownAnonymous = new()
    {
        [(typeof(WayfinderStageSurfaceController), nameof(WayfinderStageSurfaceController.Advance))] =
            "The citizen stage journey supports not-yet-signed-in applicants (GOV.UK anonymous-start " +
            "pattern — see ReferenceAppAuth.ResolveAccessProfile, which resolves an anonymous caller to " +
            "a citizen profile). Authorization is enforced downstream by the engine: AdvanceAsync is " +
            "scoped by the host-resolved AccessProfile + the instanceId, and cross-citizen isolation is " +
            "covered by the reference app's cross-citizen-isolation Playwright spec.",

        [(typeof(WayfinderStageDataController), "*")] =
            "Same anonymous-citizen-journey rationale as WayfinderStageSurfaceController.Advance — this " +
            "is the citizen stage surface's REST data plane (file download, bulk-data-review paging / " +
            "correct / revert / export). Every action resolves the caller from WayfinderServiceDesignOptions " +
            "and gates on the engine's ownership check (CanAccessInstance, via GetCurrent or the explicit " +
            "CallerOwnsInstance helper) before touching any store — that check is the package's real " +
            "access boundary here, exactly as it is for ServiceRequestStageService. See the controller's " +
            "own class remarks.",
    };

    private static IEnumerable<Type> AllControllers() =>
        typeof(WayfinderUmbracoComposer).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && (typeof(ControllerBase).IsAssignableFrom(t) || typeof(Controller).IsAssignableFrom(t)))
            .Where(t => t.Namespace?.StartsWith("Wayfinder.Umbraco") == true)
            .Distinct();

    public static TheoryData<Type> Controllers()
    {
        var data = new TheoryData<Type>();
        foreach (var c in AllControllers())
        {
            data.Add(c);
        }

        return data;
    }

    [Fact]
    public void AtLeastTheKnownControllersAreDiscovered()
    {
        AllControllers().Should().Contain(new[]
        {
            typeof(ServiceRequestPollController),
            typeof(ServiceBlueprintAuthoringController),
            typeof(ServiceRequestHubController),
            typeof(WayfinderStageSurfaceController),
            typeof(WayfinderWorklistSurfaceController),
            typeof(WayfinderStageDataController),
        }, "reflection discovery is how a new controller is forced through this contract");
    }

    [Theory]
    [MemberData(nameof(Controllers))]
    public void EveryActionDeclaresAnExplicitAuthorizationDecision(Type controller)
    {
        var classAuthorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
        var classAllowAnon = controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

        var actions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsAction)
            .ToList();

        actions.Should().NotBeEmpty("{0} is a controller with no discoverable actions — the discovery filter is wrong", controller.Name);

        foreach (var action in actions)
        {
            var actionAuthorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
            var actionAllowAnon = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            if (classAllowAnon || actionAllowAnon)
            {
                var listed = KnownAnonymous.ContainsKey((controller, action.Name))
                             || KnownAnonymous.ContainsKey((controller, "*"));
                listed.Should().BeTrue(
                    "{0}.{1} is [AllowAnonymous] — it must be on KnownAnonymous with a written reason",
                    controller.Name, action.Name);
                continue;
            }

            if (KnownImperativeAuth.ContainsKey(controller))
            {
                continue;
            }

            (classAuthorize || actionAuthorize).Should().BeTrue(
                "{0}.{1} has no [Authorize], no [AllowAnonymous], and no KnownImperativeAuth entry — " +
                "deny-by-default requires an explicit, reviewed decision at the HTTP boundary",
                controller.Name, action.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Controllers))]
    public void EveryStateChangingActionDeclaresItsAntiforgeryDisposition(Type controller)
    {
        // Umbraco Management API controllers authenticate with a backoffice bearer token, not an
        // ambient cookie — there is no cross-site-forgeable credential, so cookie antiforgery is
        // N/A by framework (Umbraco exempts them by convention, not attribute).
        if (typeof(ManagementApiControllerBase).IsAssignableFrom(controller))
        {
            return;
        }

        var classAutoValidate = controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>() is not null;
        var classIgnores = controller.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() is not null;
        var isSurfaceController = typeof(SurfaceController).IsAssignableFrom(controller);

        foreach (var action in controller
                     .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(IsAction)
                     .Where(a => IsStateChanging(a, isSurfaceController)))
        {
            var validates = classAutoValidate
                            || action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null;
            var ignores = classIgnores
                          || action.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() is not null;

            (validates || ignores).Should().BeTrue(
                "{0}.{1} changes state — it must carry [ValidateAntiForgeryToken] or an explicit " +
                "[IgnoreAntiforgeryToken] with a rationale",
                controller.Name, action.Name);
        }
    }

    private static bool IsAction(MethodInfo m) =>
        !m.IsSpecialName
        && m.GetCustomAttribute<NonActionAttribute>() is null
        && m.DeclaringType?.Namespace?.StartsWith("Wayfinder.Umbraco") == true;

    private static bool IsStateChanging(MethodInfo m, bool isSurfaceController)
    {
        var verbs = m.GetCustomAttributes<HttpMethodAttribute>()
            .SelectMany(a => a.HttpMethods)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (verbs.Count == 0)
        {
            // A SurfaceController form action with no verb attribute answers every verb, POST
            // included. A RenderController's route-hijack action (Index) is the GET render leg.
            return isSurfaceController;
        }

        return verbs.Contains("POST") || verbs.Contains("PUT") || verbs.Contains("DELETE") || verbs.Contains("PATCH");
    }
}
