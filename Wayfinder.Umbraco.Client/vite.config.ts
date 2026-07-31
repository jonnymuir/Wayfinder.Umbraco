import { defineConfig } from 'vite';

export default defineConfig({
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
