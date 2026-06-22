import { inject } from '@angular/core';
import { CanMatchFn } from '@angular/router';

import { FeatureFlagsService } from './feature-flags.service';
import { FeatureKey } from './feature-flags.types';

export function featureCanMatch(featureKey: FeatureKey): CanMatchFn {
  return () => inject(FeatureFlagsService).isEnabled(featureKey);
}
