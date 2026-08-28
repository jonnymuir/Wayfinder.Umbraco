using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Wayfinder.Engine.Abstractions;
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

    // Same string as CaseworkerQueue today, deliberately named/kept separate — this one is the
    // ClaimTypes.Role value a signed-in demo user carries (checked by referenceHome.cshtml to gate
    // which page an actor can even reach), not a Wayfinder queue key; the two concepts happen to
    // share a name in this reference app's own simple two-persona model, nothing more.
    public const string CaseworkerRole = "caseworker";

    private sealed record DemoUser(string Email, string DisplayName, string Role);

    private static readonly DemoUser Citizen = new("alex@example.test", "Alex Applicant", "citizen");
    private static readonly DemoUser Caseworker = new("casey@example.test", "Casey Caseworker", "caseworker");
    private static readonly DemoUser SecondCaseworker = new("jordan@example.test", "Jordan Caseworker", "caseworker");

    private static readonly DemoUser[] AllUsers = [Citizen, Caseworker, SecondCaseworker];

    public static ActorProfile ResolveAccessProfile(HttpContext ctx)
    {
        var role = ctx.User.FindFirst(ClaimTypes.Role)?.Value;
        var store = ctx.RequestServices.GetRequiredService<IServiceBlueprintSourceStore>();

        return role switch
        {
            // Caseworker's own queue keys resolved the same dynamic way as the citizen's below —
            // see that branch's own remarks for why a fixed [CaseworkerQueue] name doesn't hold up
            // against an independently-designed blueprint.
            "caseworker" => new ActorProfile
            {
                VisibleQueues = QueueKeysForActor(store, "caseworker"),
                StartableQueues = [],
                ActionableQueues = QueueKeysForActor(store, "caseworker"),
                RestrictToInstanceOwner = false
            },
            // Citizen, or not-yet-signed-in — a citizen's own journey is always
            // owner-restricted (see docs/guides/work-allocation.md's own mandatory-pickup rule
            // and its one real exemption), so this is also the safe default for an anonymous
            // visitor who hasn't picked a demo persona yet.
            //
            // Queue keys resolved dynamically from every saved blueprint's own declared queues
            // (whichever ones declare actor "citizen"), not hardcoded to [CitizenQueue] — confirmed
            // live this is load-bearing, not cosmetic: a blueprint designed by an MCP agent has no
            // reason to know or follow this reference app's own "citizen"-named-queue convention (a
            // real, independently-designed blueprint reasonably named its citizen queue "applicant"
            // instead, and got "Access denied to start this queue" against the old hardcoded
            // [CitizenQueue] list). An EMPTY list was tried first and rejected — confirmed live it's
            // NOT equivalent: ActorProfile.IsAllowed treats an empty list as "any queue name at
            // all," which let a citizen who'd merely submitted a request also see and act on the
            // CASEWORKER's own review stage (RestrictToInstanceOwner only checks who owns the
            // instance, not whether this queue is even meant for this actor type) — walking the
            // citizen flow through to the end landed on "Review transfer application" with
            // Approve/Reject buttons live. Resolving by the blueprint's own declared actor keeps
            // the real boundary (citizen queues only) without hardcoding any specific name.
            _ => new ActorProfile
            {
                VisibleQueues = QueueKeysForActor(store, "citizen"),
                StartableQueues = QueueKeysForActor(store, "citizen"),
                ActionableQueues = QueueKeysForActor(store, "citizen"),
                RestrictToInstanceOwner = true
            }
        };
    }

    /// <summary>
    /// Every queue key, across every saved blueprint, whose own declared <c>Actor</c> matches
    /// <paramref name="actor"/> — resolved fresh per call (a low-traffic reference app, not worth
    /// caching) rather than assuming a fixed name, since only the blueprint author actually knows
    /// what a given queue is called. GetAwaiter().GetResult() rather than making this async:
    /// WayfinderServiceDesignOptions.ResolveAccessProfile is a synchronous
    /// Func&lt;HttpContext, ActorProfile&gt; (Wayfinder.Umbraco's own delegate shape, not this
    /// app's to change) — acceptable here since IServiceBlueprintSourceStore's real implementation
    /// in this app is in-memory/DB-local, not a genuinely slow remote call.
    /// </summary>
    private static IReadOnlyList<string> QueueKeysForActor(IServiceBlueprintSourceStore store, string actor)
    {
        var summaries = store.ListAsync().GetAwaiter().GetResult();
        var keys = new List<string>();
        foreach (var summary in summaries)
        {
            var blueprint = store.LoadAsync(summary.DefinitionKey).GetAwaiter().GetResult();
            if (blueprint?.Queues is null) continue;
            keys.AddRange(blueprint.Queues
                .Where(q => string.Equals(q.Actor, actor, StringComparison.OrdinalIgnoreCase))
                .Select(q => q.Key));
        }
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
