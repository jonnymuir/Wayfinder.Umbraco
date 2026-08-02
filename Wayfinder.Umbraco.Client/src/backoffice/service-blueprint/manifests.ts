// Aggregates every extension manifest for the service blueprint backoffice screen. Registered via
// a single `type: "bundle"` entry in umbraco-package.json — Umbraco's own convention for a
// package registering many related extensions from one compiled entry point. Each `api`/`element`
// dynamic import below becomes its own code-split chunk.

import { manifests as itemManifests } from './repository/item/manifests.js';
import { manifests as detailManifests } from './repository/detail/manifests.js';
import { manifests as collectionManifests } from './collection/manifests.js';
import { manifests as entityActionManifests } from './entity-actions/manifests.js';
import { manifests as workspaceManifests } from './workspace/manifests.js';
import { manifests as rootManifests } from './root/manifests.js';
import { manifests as createModalManifests } from './create-modal/manifests.js';

export const manifests = [
  ...itemManifests,
  ...detailManifests,
  ...collectionManifests,
  ...entityActionManifests,
  ...workspaceManifests,
  ...rootManifests,
  ...createModalManifests,
];
