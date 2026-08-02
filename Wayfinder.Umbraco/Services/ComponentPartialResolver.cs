using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.ViewEngines;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Resolves which Razor partial renders a given component/field type, letting a host override
/// any type (or the fallback default) by placing a same-named file at the documented convention
/// path — <c>~/Views/Partials/Components/_Component-{Type}.cshtml</c> and
/// <c>~/Views/Partials/Fields/_Component-{Type}.cshtml</c> (see
/// docs/guides/service-request-customisation.md in Umbraco.Prism).
/// </summary>
/// <remarks>
/// <para>
/// Wayfinder.Umbraco's own built-in catalog deliberately does NOT live at that convention path —
/// it lives under <c>~/Views/Partials/_WayfinderComponents/</c> and
/// <c>~/Views/Partials/_WayfinderFields/</c> instead. This is load-bearing, not cosmetic:
/// ASP.NET Core's compiled-view lookup resolves a precompiled item at an exact virtual path
/// immediately and never falls through to check whether the consuming app ALSO defines a
/// runtime-compiled view at that identical path. If the package's own defaults occupied the same
/// path a host is documented to use for overrides, the host's file would be silently ignored —
/// confirmed live, that was the original bug this resolver replaces. Keeping the two catalogs on
/// separate paths turns "does a host override exist" into a genuine, unambiguous existence check
/// instead of an unwinnable race between two views claiming the same identity.
/// </para>
/// <para>
/// Resolution is cached indefinitely per type in a process-lifetime dictionary — the set of
/// types is small and effectively closed (the built-in catalog plus whatever a host adds), so a
/// render only ever consults the view engine once per type, not once per request. A host adding
/// a new override file requires an app restart to be picked up, the same as any other compiled
/// asset — that trade-off is what buys every subsequent render a plain dictionary lookup with no
/// view-engine call and no filesystem I/O at all.
/// </para>
/// </remarks>
public sealed class ComponentPartialResolver(ICompositeViewEngine viewEngine)
{
    private const string ComponentsHostBase    = "~/Views/Partials/Components/";
    private const string ComponentsPackageBase = "~/Views/Partials/_WayfinderComponents/";
    private const string FieldsHostBase        = "~/Views/Partials/Fields/";
    private const string FieldsPackageBase     = "~/Views/Partials/_WayfinderFields/";

    private readonly ConcurrentDictionary<string, string> _componentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _fieldCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the partial for a component <c>type</c> (kebab-case, e.g. "summary-list"),
    /// preferring a host override, then the host's own default override, then the package's
    /// built-in partial for this type, then the package's own default.
    /// </summary>
    public string ResolveComponentPartial(string? componentType) =>
        _componentCache.GetOrAdd(componentType ?? "default", type => Resolve(type, ComponentsHostBase, ComponentsPackageBase));

    /// <summary>Resolves the partial for a field <c>fieldType</c> (kebab-case, e.g. "file-upload"), same precedence as <see cref="ResolveComponentPartial"/>.</summary>
    public string ResolveFieldPartial(string fieldType) =>
        _fieldCache.GetOrAdd(fieldType, type => Resolve(type, FieldsHostBase, FieldsPackageBase));

    private string Resolve(string type, string hostBase, string packageBase)
    {
        var typeName = KebabToPascalCase(type);

        var hostSpecific = $"{hostBase}_Component-{typeName}.cshtml";
        if (ViewExists(hostSpecific)) return hostSpecific;

        var hostDefault = $"{hostBase}_Component-Default.cshtml";
        if (ViewExists(hostDefault)) return hostDefault;

        var packageSpecific = $"{packageBase}_Component-{typeName}.cshtml";
        if (ViewExists(packageSpecific)) return packageSpecific;

        return $"{packageBase}_Component-Default.cshtml";
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
