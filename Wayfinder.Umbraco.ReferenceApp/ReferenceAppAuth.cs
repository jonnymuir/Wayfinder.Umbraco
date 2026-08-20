using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// The two demo actor lanes this reference app proves — a citizen using the
/// <c>wayfinderServiceRequestStage</c> Block Grid block, and a caseworker using the
/// <c>wayfinderServiceRequestWorklist</c> one. Deliberately as plain as
/// <c>Wayfinder.ReferenceApp/Services/DemoUsers.cs</c> in the core Wayfinder repo — a fixed
/// password, no real identity provider — this app exists to prove Wayfinder.Umbraco's own
/// wiring, not to demonstrate an auth integration (see Umbraco.Prism for that).
/// </summary>
public static class ReferenceAppAuth
{
    public const string SchemeName = "WayfinderUmbracoReferenceAppCookie";
    public const string DemoPassword = "demo-password";

    public const string CitizenQueue = "citizen";
    public const string CaseworkerQueue = "caseworker";

    private sealed record DemoUser(string Email, string DisplayName, string Role);

    private static readonly DemoUser Citizen = new("alex@example.test", "Alex Applicant", "citizen");
    private static readonly DemoUser Caseworker = new("casey@example.test", "Casey Caseworker", "caseworker");
    private static readonly DemoUser SecondCaseworker = new("jordan@example.test", "Jordan Caseworker", "caseworker");

    private static readonly DemoUser[] AllUsers = [Citizen, Caseworker, SecondCaseworker];

    public static ActorProfile ResolveAccessProfile(ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        return role switch
        {
            "caseworker" => new ActorProfile
            {
                VisibleQueues = [CaseworkerQueue],
                StartableQueues = [],
                ActionableQueues = [CaseworkerQueue],
                RestrictToInstanceOwner = false
            },
            // Citizen, or not-yet-signed-in — a citizen's own journey is always
            // owner-restricted (see docs/guides/work-allocation.md's own mandatory-pickup rule
            // and its one real exemption), so this is also the safe default for an anonymous
            // visitor who hasn't picked a demo persona yet.
            _ => new ActorProfile
            {
                VisibleQueues = [CitizenQueue],
                StartableQueues = [CitizenQueue],
                ActionableQueues = [CitizenQueue],
                RestrictToInstanceOwner = true
            }
        };
    }

    public static void MapDemoLoginRoutes(WebApplication app)
    {
        app.MapGet("/demo/login", (HttpContext ctx) =>
        {
            string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

            var options = string.Join("\n", AllUsers.Select(u => $"""
                <div class="govuk-button-group">
                  <form method="post" action="/demo/login">
                    <input type="hidden" name="email" value="{Esc(u.Email)}" />
                    <button type="submit" class="govuk-button" data-module="govuk-button">{Esc(u.DisplayName)} ({Esc(u.Role)})</button>
                  </form>
                </div>
                """));

            var body = $"""
                <h1 class="govuk-heading-l">Demo login</h1>
                <p class="govuk-body">Pick a demo persona — no real password needed.</p>
                {options}
                """;

            var html = ReferenceAppPageShell.Render("Demo login", body, ctx.User);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapPost("/demo/login", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString();
            var demoUser = AllUsers.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
            if (demoUser is null)
            {
                return Results.Redirect("/demo/login");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, demoUser.Email),
                new(ClaimTypes.Name, demoUser.DisplayName),
                new(ClaimTypes.Role, demoUser.Role)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            await ctx.SignInAsync(SchemeName, new ClaimsPrincipal(identity));

            return Results.Redirect("/");
        });

        app.MapPost("/demo/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(SchemeName);
            return Results.Redirect("/");
        });
    }
}
