using System.Security.Claims;

namespace Wayfinder.Umbraco.Starter;

/// <summary>
/// The starter site's one demo staff member. Members of the public need no sign-in at all
/// (Wayfinder's anonymous visitors), so the only persona to pick is the caseworker who works the
/// staff queues. No password: this package refuses to run outside Development.
/// </summary>
public static class StarterPersonas
{
    public const string SchemeName = "WayfinderStarterCookie";

    public const string CaseworkerRole = "caseworker";

    public const string CaseworkerId = "casey@starter.example";

    public const string CaseworkerName = "Casey Caseworker";

    public static bool IsCaseworker(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && user.Identity.AuthenticationType == SchemeName
        && user.IsInRole(CaseworkerRole);

    public static ClaimsPrincipal Caseworker() =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, CaseworkerId),
                new Claim(ClaimTypes.Name, CaseworkerName),
                new Claim(ClaimTypes.Role, CaseworkerRole),
            ],
            SchemeName));
}
