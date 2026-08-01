import { defineConfig } from 'vite';

export default defineConfig({
  // Vite's modulepreload polyfill hardcodes preload URLs against `base` (default site root),
  // not against import.meta.url — these bundles are actually served from
  // /App_Plugins/Wayfinder/dist/, so an absolute-root base produced 404s the moment any chunk
  // needed preloading. A relative base makes the polyfill resolve chunk URLs relative to each
  // module's own location instead, which works under any deployment path.
  base: './',
  build: {
    // Sends compiled JS directly to Wayfinder.Umbraco's own static web assets.
    outDir: '../Wayfinder.Umbraco/wwwroot/dist',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      input: {
        // The Wayfinder section's "Service Blueprints" tab — renders <umb-collection>.
        // The manifests bundle (Collection + entity-actions + Workspace + create-modal) is
        // NOT built here — see vite.wayfinder-service-blueprint-manifests.config.ts for why it
        // needs its own build step.
        'wayfinder-service-blueprint-tab': 'src/backoffice/service-blueprint/wayfinder-service-blueprint-tab.element.ts',
      },
      output: {
        format: 'es',
        entryFileNames: '[name].js',
        chunkFileNames: '[name]-[hash].js',
      },
      // Tell Vite: "Don't bundle Umbraco's code, it will be there at runtime"
      external: [/^@umbraco-cms\/backoffice/],
    },
  },
});
