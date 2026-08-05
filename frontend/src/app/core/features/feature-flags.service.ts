import { Injectable } from '@angular/core';
import { environment } from '@environments/environment';

import { FeatureFlags, FeatureKey } from './feature-flags.types';

function lineage(featureKey: FeatureKey): FeatureKey[] {
  const segments = featureKey.split('.');
  const keys: FeatureKey[] = [];

  for (let index = 1; index <= segments.length; index += 1) {
    keys.push(segments.slice(0, index).join('.') as FeatureKey);
  }

  return keys;
}

/**
 * A feature is enabled unless it — or any of its ancestors — is explicitly off,
 * so turning off `clubs` also turns off `clubs.posts`.
 */
export function isFeatureEnabled(flags: FeatureFlags, featureKey: FeatureKey): boolean {
  for (const key of lineage(featureKey)) {
    if (flags[key] === false) {
      return false;
    }
  }

  return true;
}

@Injectable({ providedIn: 'root' })
export class FeatureFlagsService {
  private readonly flags: FeatureFlags = environment.featureFlags ?? {};

  isEnabled(featureKey: FeatureKey): boolean {
    return isFeatureEnabled(this.flags, featureKey);
  }
}
