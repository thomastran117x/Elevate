import { Injectable } from '@angular/core';
import { environment } from '@environments/environment';

import { FeatureFlags, FeatureKey } from './feature-flags.types';

@Injectable({ providedIn: 'root' })
export class FeatureFlagsService {
  private readonly flags: FeatureFlags = environment.featureFlags ?? {};

  isEnabled(featureKey: FeatureKey): boolean {
    for (const key of this.lineage(featureKey)) {
      if (this.flags[key] === false) {
        return false;
      }
    }

    return true;
  }

  private lineage(featureKey: FeatureKey): FeatureKey[] {
    const segments = featureKey.split('.');
    const keys: FeatureKey[] = [];

    for (let index = 1; index <= segments.length; index += 1) {
      keys.push(segments.slice(0, index).join('.') as FeatureKey);
    }

    return keys;
  }
}
