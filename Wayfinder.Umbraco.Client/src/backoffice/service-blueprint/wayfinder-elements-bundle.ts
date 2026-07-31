// Loads the editor's self-contained ES module bundle by URL at runtime — the same
// wayfinder-elements.js the Wayfinder.Editor NuGet package already serves as a static web asset
// (see Wayfinder.Editor.Client/src/service-blueprint-editor/README.md's "Bundle reference").
// Not npm-installed: there is no @wayfinder/editor-client package, so nothing here is a
// compile-time dependency of this project — the browser fetches the module the first time it's
// needed and every subsequent call reuses the same import() promise.
//
// Default ASP.NET Core static web asset base path for a referenced RCL is
// `/_content/{PackageId}/...` — Wayfinder.Editor.csproj declares no
// <StaticWebAssetBasePath> override, so this is that default, not a Wayfinder.Umbraco-side
// convention of its own.
const WAYFINDER_ELEMENTS_URL = '/_content/Wayfinder.Editor/dist/wayfinder-elements.js';

let modulePromise: Promise<any> | undefined;

/**
 * Registers `<wayfinder-service-blueprint-editor>` (and its siblings) as custom elements the
 * first time it's called, and returns the module's runtime exports (ServiceBlueprintSaveError,
 * hydrateServiceBlueprintDefinition, etc.) every time.
 */
export function loadWayfinderElements(): Promise<any> {
  modulePromise ??= import(/* @vite-ignore */ WAYFINDER_ELEMENTS_URL);
  return modulePromise;
}
