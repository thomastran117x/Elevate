import { TestBed } from '@angular/core/testing';

import { environment } from '@environments/environment';

import { FEATURE_KEYS } from './feature-flags.types';
import { FeatureFlagsService } from './feature-flags.service';

describe('FeatureFlagsService', () => {
  const originalFeatureFlags = environment.featureFlags;

  afterEach(() => {
    environment.featureFlags = { ...originalFeatureFlags };
    TestBed.resetTestingModule();
  });

  it('defaults missing flags to enabled', () => {
    environment.featureFlags = {};
    TestBed.configureTestingModule({ providers: [FeatureFlagsService] });

    const service = TestBed.inject(FeatureFlagsService);

    expect(service.isEnabled(FEATURE_KEYS.eventsAnalytics)).toBeTrue();
  });

  it('disables descendants when a parent flag is off', () => {
    environment.featureFlags = {
      [FEATURE_KEYS.events]: false,
      [FEATURE_KEYS.eventsInvitations]: true,
    };
    TestBed.configureTestingModule({ providers: [FeatureFlagsService] });

    const service = TestBed.inject(FeatureFlagsService);

    expect(service.isEnabled(FEATURE_KEYS.eventsInvitations)).toBeFalse();
  });

  it('allows sibling features to remain enabled when a subfeature is off', () => {
    environment.featureFlags = {
      [FEATURE_KEYS.eventsInvitations]: false,
    };
    TestBed.configureTestingModule({ providers: [FeatureFlagsService] });

    const service = TestBed.inject(FeatureFlagsService);

    expect(service.isEnabled(FEATURE_KEYS.events)).toBeTrue();
    expect(service.isEnabled(FEATURE_KEYS.eventsRegistration)).toBeTrue();
    expect(service.isEnabled(FEATURE_KEYS.eventsInvitations)).toBeFalse();
  });
});
