import { AccountSecurityEvent } from '../../../../services/security-history.service';
import { toSecurityActivityView } from './security-activity-labels';

describe('toSecurityActivityView', () => {
  function entry(overrides: Partial<AccountSecurityEvent> = {}): AccountSecurityEvent {
    return {
      id: 1,
      kind: 'signed_in',
      occurredAt: '2026-10-20T12:00:00Z',
      method: null,
      factor: null,
      reason: null,
      client: 'unknown',
      deviceClass: 'unknown',
      network: null,
      initiatedByAdmin: false,
      reference: null,
      ...overrides,
    };
  }

  const cases: [Partial<AccountSecurityEvent>, string, string | null][] = [
    [{ kind: 'signed_in', method: 'password' }, 'Signed in', 'Signed in with your password.'],
    [{ kind: 'signed_in', method: 'google' }, 'Signed in', 'Signed in with Google.'],
    [{ kind: 'signed_in', method: 'carrier-pigeon' }, 'Signed in', null],
    [
      { kind: 'new_device_challenge' },
      'New device verification requested',
      'A sign-in from a device we did not recognise was asked to confirm by email.',
    ],
    [
      { kind: 'sign_in_challenge' },
      'Two-step verification requested',
      'A sign-in with your password was asked for a second step before continuing.',
    ],
    [
      { kind: 'password_reset' },
      'Password reset',
      'Your password was reset with an emailed link or code.',
    ],
    [{ kind: 'password_changed' }, 'Password changed', null],
    [{ kind: 'email_changed' }, 'Email address changed', null],
    [{ kind: 'mfa_enabled', factor: 'totp' }, 'Authenticator app verification turned on', null],
    [{ kind: 'mfa_disabled', factor: 'sms' }, 'Text message (SMS) verification turned off', null],
    [{ kind: 'mfa_removed', factor: null }, 'Two-step verification removed', null],
    [
      { kind: 'provider_linked', method: 'microsoft' },
      'Microsoft sign-in linked',
      'You can now sign in with this provider.',
    ],
    [
      { kind: 'provider_linked', method: null },
      'Sign-in provider sign-in linked',
      'You can now sign in with this provider.',
    ],
    [{ kind: 'signed_out' }, 'Signed out', null],
    [
      { kind: 'sessions_revoked', reason: 'email_changed' },
      'Signed out everywhere',
      'Every session was signed out after your email address was changed.',
    ],
    [
      { kind: 'sessions_revoked', reason: null },
      'Signed out everywhere',
      'Every session was signed out.',
    ],
    [
      { kind: 'session_ended_for_security' },
      'Session ended for security',
      'A session no longer matched the device or account it was issued to, so it was ended.',
    ],
    [
      { kind: 'refresh_token_reuse_detected' },
      'Suspicious sign-in token use blocked',
      'A sign-in token was used again after it had been replaced, so that session was signed out.',
    ],
    [{ kind: 'account_disabled' }, 'Account disabled', 'Every session was signed out.'],
    [{ kind: 'account_enabled' }, 'Account re-enabled', null],
    [{ kind: 'something_added_later' }, 'Security activity', null],
  ];

  for (const [overrides, title, detail] of cases) {
    it(`describes ${overrides.kind} (${overrides.method ?? overrides.factor ?? overrides.reason ?? 'plain'})`, () => {
      const view = toSecurityActivityView(entry(overrides));

      expect(view.title).toBe(title);
      expect(view.detail).toBe(detail);
    });
  }

  it('joins only the context it recognises', () => {
    expect(
      toSecurityActivityView(
        entry({ client: 'firefox', deviceClass: 'mobile', network: '203.0.113.0/24' }),
      ).context,
    ).toBe('Firefox · Phone · 203.0.113.0/24');
    expect(
      toSecurityActivityView(entry({ client: 'api_client', deviceClass: 'api_client' })).context,
    ).toBe('API client');
    expect(toSecurityActivityView(entry()).context).toBeNull();
  });

  it('flags the changes an owner should double-check', () => {
    expect(
      toSecurityActivityView(entry({ kind: 'refresh_token_reuse_detected' })).important,
    ).toBeTrue();
    expect(toSecurityActivityView(entry({ kind: 'mfa_disabled' })).important).toBeTrue();
    expect(toSecurityActivityView(entry({ kind: 'signed_in' })).important).toBeFalse();
  });

  it('carries the reference, timestamp, and administrator flag through', () => {
    const view = toSecurityActivityView(
      entry({ id: 9, reference: 'req-9', initiatedByAdmin: true, kind: 'account_disabled' }),
    );

    expect(view).toEqual(
      jasmine.objectContaining({
        id: 9,
        reference: 'req-9',
        initiatedByAdmin: true,
        occurredAt: '2026-10-20T12:00:00Z',
      }),
    );
  });
});
