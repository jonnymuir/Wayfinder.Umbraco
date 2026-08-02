import { defineConfig } from 'vite';

// A single entry now: wayfinder-service-blueprint-manifests.js — pure-data `export const
// manifests = [...]`, no side-effecting code (no @customElement decorators, nothing that
// registers itself globally), read by Umbraco's own "bundle" extension loader via
// `Object.keys(importedModule)`, which cares about the *exported values*, not the compiled
// export *names*. There used to be a second entry (the "Wayfinder" section's own dashboard
// tab element) built by this same config; that section — and the tab element that worked
// around its lack of a menu system — is gone now that Blueprints is mounted into Umbraco's
// built-in Settings section instead (see backoffice/service-blueprint/root/manifests.ts).
//
// `preserveEntrySignatures: 'strict'` matters here: without it, Rollup is free to
// rename/reassign this entry's exports arbitrarily, and the built module can come out as an
// empty chunk instead of the `manifests` array (confirmed live — the same failure
// UmbracoPrism.Client's own vite.cms-service-blueprint-manifests.config.ts documents).
export default defineConfig({
  // Vite's modulepreload polyfill hardcodes preload URLs against `base` (default site root),
  // not against import.meta.url — this bundle is actually served from
  // /App_Plugins/Wayfinder/dist/, so an absolute-root base produced 404s the moment any chunk
  // needed preloading. A relative base makes the polyfill resolve chunk URLs relative to each
  // module's own location instead, which works under any deployment path.
  base: './',
  build: {
    outDir: '../Wayfinder.Umbraco/wwwroot/dist',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      input: {
        'wayfinder-service-blueprint-manifests': 'src/backoffice/service-blueprint/manifests.ts',
      },
      output: {
        format: 'es',
        entryFileNames: '[name].js',
        chunkFileNames: '[name]-[hash].js',
      },
      // Tell Vite: "Don't bundle Umbraco's code, it will be there at runtime"
      external: [/^@umbraco-cms\/backoffice/],
      preserveEntrySignatures: 'strict',
    },
  },
});
