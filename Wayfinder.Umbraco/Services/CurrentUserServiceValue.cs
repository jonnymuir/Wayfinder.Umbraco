using System.Security.Claims;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// The built-in <c>user</c> service value: who is signed in on the current request, for a
/// blueprint to read through a <c>source: "service"</c> calculation field named <c>user</c>.
/// A blueprint declares it and pre-fills inputs with <c>defaultFrom</c>, with no host code:
/// <code>
/// "calculations": { "fields": { "user": { "source": "service",
///     "shape": { "name": { "valueKind": "string" }, "email": { "valueKind": "string" } } } } }
/// { "type": "text", "fieldKey": "applicantName", "defaultFrom": "user.name" }
/// </code>
/// Both properties are always present and are empty strings for an anonymous visitor or a
/// missing claim, because the engine treats a declared service field the host does not supply
/// as an error.
/// </summary>
public static class CurrentUserServiceValue
{
    /// <summary>The calculation field name a blueprint declares to receive this value.</summary>
    public const string FieldName = "user";

    public static IReadOnlyDictionary<string, object?> Build(ClaimsPrincipal? user)
    {
        var signedIn = user?.Identity?.IsAuthenticated ?? false;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = signedIn ? FirstClaim(user!, ClaimTypes.Name, "name") ?? user!.Identity!.Name ?? "" : "",
            ["email"] = signedIn ? FirstClaim(user!, ClaimTypes.Email, "email") ?? "" : "",
        };
    }

    private static string? FirstClaim(ClaimsPrincipal user, params string[] types)
    {
        foreach (var type in types)
        {
            var value = user.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
