import { defineConfig } from 'vite';

// A dedicated build step for wayfinder-service-blueprint-manifests.js only. This entry's sole
// content is a pure-data `export const manifests = [...]` — no side-effecting code (no
// @customElement decorators, nothing that registers itself globally) — read by Umbraco's own
// "bundle" extension loader via `Object.keys(importedModule)`, which cares about the *exported
// values*, not the compiled export *names*.
//
// Building this alongside vite.config.ts's tab entry (which has no `preserveEntrySignatures`
// set) lets Rollup rename/reassign the entry's export arbitrarily — confirmed live here too
// (the same failure UmbracoPrism.Client's own vite.cms-service-blueprint-manifests.config.ts
// documents): the built module came out as an empty chunk instead of the `manifests` array.
// `preserveEntrySignatures: 'strict'` fixes it — but set globally, it re-chunks every entry, so
// this one entry gets its own build step instead of forcing that setting onto the tab entry too.
export default defineConfig({
  // See vite.config.ts's own `base` comment — same fix needed here, this entry preloads chunks too.
  base: './',
  build: {
    outDir: '../Wayfinder.Umbraco/wwwroot/dist',
    // Never wipe the directory here — vite.config.ts's own build already populated it with the
    // tab entry; this step only adds to it.
    emptyOutDir: false,
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
      external: [/^@umbraco-cms\/backoffice/],
      preserveEntrySignatures: 'strict',
    },
  },
});
