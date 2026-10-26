using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wayfinder.Engine.Abstractions;

namespace Wayfinder.Umbraco.Starter;

/// <summary>
/// The starter's own pages: one per saved blueprint, so a service designed in conversation can be
/// tried the moment it is saved with no content to create, plus the caseworker's work queue and
/// the demo sign-in. Anonymous by design, the same as any public service page: what each person
/// can see and do is decided by <see cref="StarterAccess"/> and enforced by the engine.
/// </summary>
[AllowAnonymous]
public class StarterController(IProcessManager processManager) : Controller
{
    [HttpGet(StarterRoutes.Services + "/{definitionKey}")]
    public IActionResult Service(string definitionKey) =>
        processManager.GetDefinition(definitionKey) is { } blueprint
            ? View("~/Views/WayfinderStarter/Service.cshtml", blueprint)
            : NotFound();

    [HttpGet(StarterRoutes.WorkQueue)]
    public IActionResult WorkQueue() => View("~/Views/WayfinderStarter/WorkQueue.cshtml");

    [HttpPost(StarterRoutes.SignIn)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignIn()
    {
        await HttpContext.SignInAsync(StarterPersonas.SchemeName, StarterPersonas.Caseworker());
        return Redirect(StarterRoutes.WorkQueue);
    }

    [HttpPost(StarterRoutes.SignOut)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOut(string? returnUrl)
    {
        await HttpContext.SignOutAsync(StarterPersonas.SchemeName);
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
