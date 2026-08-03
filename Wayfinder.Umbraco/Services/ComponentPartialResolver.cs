using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.ViewEngines;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Resolves a host's own Razor override for a given component/field type, if one exists, at the
/// documented convention path — <c>~/Views/Partials/Components/_Component-{Type}.cshtml</c> and
/// <c>~/Views/Partials/Fields/_Component-{Type}.cshtml</c> (see
/// docs/guides/service-request-customisation.md in Umbraco.Prism). Returns <c>null</c> when no
/// host override exists, in which case <c>ComponentTagHelper</c> falls through to
/// <c>Wayfinder.Rendering.GovUk</c>'s <c>GovUkComponentRenderer</c> — the shared package's own
/// built-in catalog plus whatever Wayfinder.Umbraco itself has registered as overrides there
/// (<c>file-upload</c>, <c>slider</c>, <c>stat-group</c>, <c>chart</c> — genuinely richer than
/// the shared package's deliberately plain defaults, so this package keeps its own markup for
/// those specific types rather than downgrading to the shared default).
/// </summary>
/// <remarks>
/// This resolver used to also own the *package's own* built-in catalog, at a deliberately
/// different virtual path than the host-override convention — ASP.NET Core's compiled-view
/// lookup resolves a precompiled item at an exact virtual path immediately and never falls
/// through to check whether the host ALSO defines a runtime-compiled view at that identical
/// path, so sharing one path would have silently shadowed a host's own override (confirmed
/// live, historically). Since the built-in catalog is no longer Razor at all — it moved to
/// <c>Wayfinder.Rendering.GovUk</c>, plain C#, no ViewEngine involved — that entire class of bug
/// is gone by construction: there's nothing left at any package-owned virtual path to collide
/// with a host's own file.
/// </remarks>
public sealed class ComponentPartialResolver(ICompositeViewEngine viewEngine)
{
    private const string ComponentsHostBase = "~/Views/Partials/Components/";
    private const string FieldsHostBase = "~/Views/Partials/Fields/";

    private readonly ConcurrentDictionary<string, string?> _componentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _fieldCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a host's own override view for a component <c>type</c> (kebab-case,
    /// e.g. "summary-list") — its own type-specific file, then its own catch-all default, then
    /// <c>null</c> if neither exists.</summary>
    public string? ResolveComponentHostOverride(string? componentType) =>
        _componentCache.GetOrAdd(componentType ?? "default", type => Resolve(type, ComponentsHostBase));

    /// <summary>Resolves a host's own override view for a field <c>fieldType</c> (kebab-case,
    /// e.g. "file-upload"), same precedence as <see cref="ResolveComponentHostOverride"/>.</summary>
    public string? ResolveFieldHostOverride(string fieldType) =>
        _fieldCache.GetOrAdd(fieldType, type => Resolve(type, FieldsHostBase));

    private string? Resolve(string type, string hostBase)
    {
        var typeName = KebabToPascalCase(type);

        var hostSpecific = $"{hostBase}_Component-{typeName}.cshtml";
        if (ViewExists(hostSpecific)) return hostSpecific;

        var hostDefault = $"{hostBase}_Component-Default.cshtml";
        return ViewExists(hostDefault) ? hostDefault : null;
    }

    private bool ViewExists(string viewPath) =>
        viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false).Success;

    /// <summary>Converts a kebab-case string to PascalCase. "summary-list" → "SummaryList", "fieldset" → "Fieldset".</summary>
    private static string KebabToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return "Default";

        var parts = input.Split('-');
        return string.Concat(parts.Select(p =>
            string.IsNullOrEmpty(p) ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
