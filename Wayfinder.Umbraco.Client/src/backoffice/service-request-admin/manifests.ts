// Aggregates every extension manifest for the service request admin backoffice screen. Registered
// via its own `type: "bundle"` entry in umbraco-package.json, separate from the service-blueprint
// bundle — two unrelated features, two independently-loadable compiled entries.

import { manifests as collectionManifests } from './collection/manifests.js';
import { manifests as entityActionManifests } from './entity-actions/manifests.js';
import { manifests as rootManifests } from './root/manifests.js';

export const manifests = [
  ...collectionManifests,
  ...entityActionManifests,
  ...rootManifests,
];
