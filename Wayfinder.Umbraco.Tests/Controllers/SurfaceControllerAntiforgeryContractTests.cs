using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Wayfinder.Umbraco.Controllers;

namespace Wayfinder.Umbraco.Tests.Controllers;

/// <summary>
/// SECURITY REGRESSION: the two browser-form SurfaceControllers change server state from a
/// cookie-authenticated POST, so every non-GET action on them must validate the antiforgery
/// token — otherwise a cross-site page could forge a pickup / putback / stage-advance as a
/// signed-in caseworker. This test goes red if a <c>[ValidateAntiForgeryToken]</c> is dropped
/// or a new unprotected POST/PUT/DELETE action is added.
/// </summary>
public class SurfaceControllerAntiforgeryContractTests
{
    public static TheoryData<Type> BrowserFormControllers() =>
    [
        typeof(WayfinderStageSurfaceController),
        typeof(WayfinderWorklistSurfaceController),
    ];

    [Theory]
    [MemberData(nameof(BrowserFormControllers))]
    public void EveryStateChangingAction_ValidatesTheAntiforgeryToken(Type controller)
    {
        var stateChanging = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsStateChangingAction)
            .ToList();

        stateChanging.Should().NotBeEmpty("the test targets the wrong type if a form controller has no POST actions");

        var classValidates =
            controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>() is not null;

        foreach (var action in stateChanging)
        {
            var actionValidates =
                action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null;

            (classValidates || actionValidates).Should().BeTrue(
                "{0}.{1} handles a state-changing request and must carry [ValidateAntiForgeryToken]",
                controller.Name, action.Name);
        }
    }

    private static bool IsStateChangingAction(MethodInfo method)
    {
        if (method.IsSpecialName || method.GetCustomAttribute<NonActionAttribute>() is not null)
        {
            return false;
        }

        var verbs = method.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // No verb attribute on a SurfaceController action means it responds to any verb, POST included.
        if (verbs.Count == 0)
        {
            return true;
        }

        return verbs.Contains("POST") || verbs.Contains("PUT") || verbs.Contains("DELETE") || verbs.Contains("PATCH");
    }
}
