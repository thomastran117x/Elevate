import { deviceDescription } from './device-labels';
import { PendingStepUp } from './pending-step-up';

describe('deviceDescription', () => {
  it('names the client and the device class', () => {
    expect(deviceDescription('chrome', 'desktop')).toBe('Chrome · Desktop');
    expect(deviceDescription('api_client', 'unknown')).toBe('API client');
    expect(deviceDescription('unknown', 'mobile')).toBe('Phone');
    expect(deviceDescription('unknown', 'unknown')).toBe('Unknown device');
  });
});

describe('PendingStepUp', () => {
  it('runs the held action once verification succeeds', () => {
    const pending = new PendingStepUp();
    const action = jasmine.createSpy('action');

    pending.request(action);
    expect(pending.open()).toBeTrue();
    pending.verified();
    pending.verified();

    expect(action).toHaveBeenCalledTimes(1);
    expect(pending.open()).toBeFalse();
  });

  it('drops the held action when verification is dismissed', () => {
    const pending = new PendingStepUp();
    const action = jasmine.createSpy('action');

    pending.request(action);
    pending.dismissed();
    pending.verified();

    expect(action).not.toHaveBeenCalled();
    expect(pending.open()).toBeFalse();
  });
});
