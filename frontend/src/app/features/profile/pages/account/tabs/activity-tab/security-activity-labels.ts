import { AccountSecurityEvent } from '../../../../services/security-history.service';

/** How one security history entry reads on the Activity tab. */
export interface SecurityActivityView {
  id: number;
  title: string;
  detail: string | null;
  /** Coarse client, device, and masked network, e.g. "Chrome · Desktop · 203.0.113.0/24". */
  context: string | null;
  occurredAt: string;
  /** A change the owner should double-check if they did not make it. */
  important: boolean;
  initiatedByAdmin: boolean;
  reference: string | null;
}

const METHOD_LABELS: Record<string, string> = {
  password: 'with your password',
  google: 'with Google',
  microsoft: 'with Microsoft',
  email: 'with an emailed link or code',
  sms: 'with a text message code',
  totp: 'with an authenticator app',
};

const PROVIDER_LABELS: Record<string, string> = {
  google: 'Google',
  microsoft: 'Microsoft',
};

const FACTOR_LABELS: Record<string, string> = {
  sms: 'Text message (SMS)',
  totp: 'Authenticator app',
};

const REVOCATION_REASONS: Record<string, string> = {
  password_changed: 'after your password was changed',
  password_reset: 'after your password was reset',
  email_changed: 'after your email address was changed',
};

const CLIENT_LABELS: Record<string, string> = {
  chrome: 'Chrome',
  edge: 'Edge',
  firefox: 'Firefox',
  safari: 'Safari',
  opera: 'Opera',
  api_client: 'API client',
  other: 'Other app',
};

const DEVICE_LABELS: Record<string, string> = {
  desktop: 'Desktop',
  mobile: 'Phone',
  tablet: 'Tablet',
};

const IMPORTANT_KINDS = new Set([
  'password_changed',
  'password_reset',
  'email_changed',
  'mfa_disabled',
  'mfa_removed',
  'provider_linked',
  'refresh_token_reuse_detected',
  'account_disabled',
]);

function factorLabel(factor: string | null): string {
  return (factor && FACTOR_LABELS[factor]) || 'Two-step';
}

function describe(entry: AccountSecurityEvent): { title: string; detail: string | null } {
  switch (entry.kind) {
    case 'signed_in':
      return {
        title: 'Signed in',
        detail:
          entry.method && METHOD_LABELS[entry.method]
            ? `Signed in ${METHOD_LABELS[entry.method]}.`
            : null,
      };
    case 'new_device_challenge':
      return {
        title: 'New device verification requested',
        detail: 'A sign-in from a device we did not recognise was asked to confirm by email.',
      };
    case 'sign_in_challenge':
      return {
        title: 'Two-step verification requested',
        detail: 'A sign-in with your password was asked for a second step before continuing.',
      };
    case 'password_reset':
      return {
        title: 'Password reset',
        detail: 'Your password was reset with an emailed link or code.',
      };
    case 'password_changed':
      return { title: 'Password changed', detail: null };
    case 'email_changed':
      return { title: 'Email address changed', detail: null };
    case 'mfa_enabled':
      return { title: `${factorLabel(entry.factor)} verification turned on`, detail: null };
    case 'mfa_disabled':
      return { title: `${factorLabel(entry.factor)} verification turned off`, detail: null };
    case 'mfa_removed':
      return { title: `${factorLabel(entry.factor)} verification removed`, detail: null };
    case 'provider_linked':
      return {
        title: `${(entry.method && PROVIDER_LABELS[entry.method]) || 'Sign-in provider'} sign-in linked`,
        detail: 'You can now sign in with this provider.',
      };
    case 'signed_out':
      return { title: 'Signed out', detail: null };
    case 'sessions_revoked':
      return {
        title: 'Signed out everywhere',
        detail:
          entry.reason && REVOCATION_REASONS[entry.reason]
            ? `Every session was signed out ${REVOCATION_REASONS[entry.reason]}.`
            : 'Every session was signed out.',
      };
    case 'session_ended_for_security':
      return {
        title: 'Session ended for security',
        detail:
          'A session no longer matched the device or account it was issued to, so it was ended.',
      };
    case 'refresh_token_reuse_detected':
      return {
        title: 'Suspicious sign-in token use blocked',
        detail:
          'A sign-in token was used again after it had been replaced, so that session was signed out.',
      };
    case 'account_disabled':
      return { title: 'Account disabled', detail: 'Every session was signed out.' };
    case 'account_enabled':
      return { title: 'Account re-enabled', detail: null };
    default:
      return { title: 'Security activity', detail: null };
  }
}

function contextLine(entry: AccountSecurityEvent): string | null {
  const parts = [
    CLIENT_LABELS[entry.client],
    DEVICE_LABELS[entry.deviceClass],
    entry.network,
  ].filter((part): part is string => !!part);
  return parts.length > 0 ? parts.join(' · ') : null;
}

export function toSecurityActivityView(entry: AccountSecurityEvent): SecurityActivityView {
  const { title, detail } = describe(entry);
  return {
    id: entry.id,
    title,
    detail,
    context: contextLine(entry),
    occurredAt: entry.occurredAt,
    important: IMPORTANT_KINDS.has(entry.kind),
    initiatedByAdmin: entry.initiatedByAdmin,
    reference: entry.reference,
  };
}
