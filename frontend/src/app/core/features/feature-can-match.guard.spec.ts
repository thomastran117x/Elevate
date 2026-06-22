import { TestBed } from '@angular/core/testing';
import { Route } from '@angular/router';

import { environment } from '@environments/environment';

import { featureCanMatch } from './feature-can-match.guard';
import { FEATURE_KEYS } from './feature-flags.types';
import { FeatureFlagsService } from './feature-flags.service';

describe('featureCanMatch', () => {
  const originalFeatureFlags = environment.featureFlags;
  const route = {} as Route;

  afterEach(() => {
    environment.featureFlags = { ...originalFeatureFlags };
    TestBed.resetTestingModule();
  });

  it('blocks disabled routes', () => {
    environment.featureFlags = {
      [FEATURE_KEYS.events]: false,
    };
    TestBed.configureTestingModule({ providers: [FeatureFlagsService] });

    const result = TestBed.runInInjectionContext(() =>
      featureCanMatch(FEATURE_KEYS.events)(route, []),
    );

    expect(result).toBeFalse();
  });

  it('allows enabled routes', () => {
    environment.featureFlags = {
      [FEATURE_KEYS.events]: true,
    };
    TestBed.configureTestingModule({ providers: [FeatureFlagsService] });

    const result = TestBed.runInInjectionContext(() =>
      featureCanMatch(FEATURE_KEYS.events)(route, []),
    );

    expect(result).toBeTrue();
  });
});
