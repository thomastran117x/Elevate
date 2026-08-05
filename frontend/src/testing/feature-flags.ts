import { Provider } from '@angular/core';

import { FeatureFlagsService, isFeatureEnabled } from '../app/core/features/feature-flags.service';
import { FeatureFlags, FeatureKey } from '../app/core/features/feature-flags.types';

/**
 * Overrides `FeatureFlagsService` with a fake backed by the real cascade logic.
 *
 * Prefer this over mutating `environment.featureFlags`: the environment object
 * is module-global, so a spec that forgets to restore it leaks into every other
 * spec in the run.
 */
export function provideFeatureFlags(flags: FeatureFlags): Provider {
  return {
    provide: FeatureFlagsService,
    useValue: {
      isEnabled: (featureKey: FeatureKey) => isFeatureEnabled(flags, featureKey),
    } satisfies Pick<FeatureFlagsService, 'isEnabled'>,
  };
}
