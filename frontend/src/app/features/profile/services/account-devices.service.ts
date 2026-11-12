import { Injectable } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';

import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import {
  UnknownRecord,
  asRecord,
  readBoolean,
  readNullableString,
  readNumber,
  readString,
} from '../../../core/models/payload-casing';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { environment } from '../../../../environments/environment';

/**
 * One of the signed-in user's live sessions.
 *
 * Classifications are snake_case names from closed sets on the server, the same ones the security
 * history uses, so the view can share its labels.
 */
export interface AccountSession {
  id: string;
  /** The session making the request: this browser. */
  current: boolean;
  /** `browser` or `api`. */
  transport: string;
  client: string;
  deviceClass: string;
  /** The last source network prefix, e.g. `203.0.113.0/24`. Never a full address. */
  network: string | null;
  rememberMe: boolean;
  createdAt: string;
  lastSeenAt: string;
  expiresAt: string;
}

/** One device that skips sign-in verification until {@link expiresAt}. */
export interface TrustedDevice {
  id: number;
  /** This browser presented the device's token. */
  current: boolean;
  client: string;
  deviceClass: string;
  network: string | null;
  createdAt: string;
  lastSeenAt: string;
  expiresAt: string;
}

export interface SessionRevocation {
  revokedCount: number;
  /** This browser's own session was ended, so it is signed out. */
  currentSessionRevoked: boolean;
}

export interface TrustedDeviceRevocation {
  revokedCount: number;
  /** This browser was forgotten, so its next sign-in asks for verification. */
  currentDeviceRevoked: boolean;
}

function readTimes(
  source: UnknownRecord,
): { createdAt: string; lastSeenAt: string; expiresAt: string } | null {
  const createdAt = readString(source, 'CreatedAt', 'createdAt');
  const lastSeenAt = readString(source, 'LastSeenAt', 'lastSeenAt');
  const expiresAt = readString(source, 'ExpiresAt', 'expiresAt');
  return createdAt && lastSeenAt && expiresAt ? { createdAt, lastSeenAt, expiresAt } : null;
}

function readList(value: unknown, pascal: string, camel: string): unknown[] {
  const source = asRecord(value);
  const items = source ? (source[pascal] ?? source[camel]) : undefined;
  return Array.isArray(items) ? items : [];
}

function normalizeSession(value: unknown): AccountSession | null {
  const source = asRecord(value);
  const id = source ? readString(source, 'Id', 'id') : undefined;
  const times = source ? readTimes(source) : null;
  if (!source || !id || !times) {
    return null;
  }

  return {
    id,
    current: readBoolean(source, 'Current', 'current') ?? false,
    transport: readString(source, 'Transport', 'transport') ?? 'browser',
    client: readString(source, 'Client', 'client') ?? 'unknown',
    deviceClass: readString(source, 'DeviceClass', 'deviceClass') ?? 'unknown',
    network: readNullableString(source, 'Network', 'network') ?? null,
    rememberMe: readBoolean(source, 'RememberMe', 'rememberMe') ?? false,
    ...times,
  };
}

function normalizeTrustedDevice(value: unknown): TrustedDevice | null {
  const source = asRecord(value);
  const id = source ? readNumber(source, 'Id', 'id') : undefined;
  const times = source ? readTimes(source) : null;
  if (!source || id === undefined || !times) {
    return null;
  }

  return {
    id,
    current: readBoolean(source, 'Current', 'current') ?? false,
    client: readString(source, 'Client', 'client') ?? 'unknown',
    deviceClass: readString(source, 'DeviceClass', 'deviceClass') ?? 'unknown',
    network: readNullableString(source, 'Network', 'network') ?? null,
    ...times,
  };
}

/** Accepts either casing and drops rows missing what a row needs, so one cannot blank the list. */
export function normalizeSessions(value: unknown): AccountSession[] {
  return readList(value, 'Sessions', 'sessions')
    .map(normalizeSession)
    .filter((session): session is AccountSession => session !== null);
}

/** Accepts either casing and drops rows missing what a row needs, so one cannot blank the list. */
export function normalizeTrustedDevices(value: unknown): TrustedDevice[] {
  return readList(value, 'Devices', 'devices')
    .map(normalizeTrustedDevice)
    .filter((device): device is TrustedDevice => device !== null);
}

export function normalizeSessionRevocation(value: unknown): SessionRevocation | null {
  const source = asRecord(value);
  const currentSessionRevoked = source
    ? readBoolean(source, 'CurrentSessionRevoked', 'currentSessionRevoked')
    : undefined;
  if (!source || currentSessionRevoked === undefined) {
    return null;
  }

  return {
    revokedCount: readNumber(source, 'RevokedCount', 'revokedCount') ?? 0,
    currentSessionRevoked,
  };
}

export function normalizeTrustedDeviceRevocation(value: unknown): TrustedDeviceRevocation | null {
  const source = asRecord(value);
  const currentDeviceRevoked = source
    ? readBoolean(source, 'CurrentDeviceRevoked', 'currentDeviceRevoked')
    : undefined;
  if (!source || currentDeviceRevoked === undefined) {
    return null;
  }

  return {
    revokedCount: readNumber(source, 'RevokedCount', 'revokedCount') ?? 0,
    currentDeviceRevoked,
  };
}

/**
 * The signed-in owner's sessions and trusted devices. Listing needs no step-up; every revocation
 * does, and fails with `MFA_REQUIRED` without a fresh proof.
 */
@Injectable({ providedIn: 'root' })
export class AccountDevicesService {
  private readonly sessionsUrl = `${environment.backendUrl}/auth/sessions`;
  private readonly devicesUrl = `${environment.backendUrl}/auth/devices`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
  ) {}

  listSessions(): Observable<AccountSession[]> {
    return this.withCsrf(() =>
      this.api.get<ApiEnvelope<unknown>>(this.sessionsUrl, { withCredentials: true }),
    ).pipe(map((res) => normalizeSessions(this.data(res))));
  }

  revokeSession(sessionId: string): Observable<SessionRevocation> {
    return this.revokeSessions(`${this.sessionsUrl}/${encodeURIComponent(sessionId)}`);
  }

  revokeOtherSessions(): Observable<SessionRevocation> {
    return this.revokeSessions(`${this.sessionsUrl}/others`);
  }

  revokeAllSessions(): Observable<SessionRevocation> {
    return this.revokeSessions(this.sessionsUrl);
  }

  listTrustedDevices(): Observable<TrustedDevice[]> {
    return this.withCsrf(() =>
      this.api.get<ApiEnvelope<unknown>>(this.devicesUrl, { withCredentials: true }),
    ).pipe(map((res) => normalizeTrustedDevices(this.data(res))));
  }

  revokeTrustedDevice(deviceId: number): Observable<TrustedDeviceRevocation> {
    return this.revokeDevices(`${this.devicesUrl}/${deviceId}`);
  }

  revokeAllTrustedDevices(): Observable<TrustedDeviceRevocation> {
    return this.revokeDevices(this.devicesUrl);
  }

  private revokeSessions(url: string): Observable<SessionRevocation> {
    return this.withCsrf(() =>
      this.api.delete<ApiEnvelope<unknown>>(url, { withCredentials: true }),
    ).pipe(
      map((res) => this.require(normalizeSessionRevocation(this.data(res)), 'Session revocation')),
    );
  }

  private revokeDevices(url: string): Observable<TrustedDeviceRevocation> {
    return this.withCsrf(() =>
      this.api.delete<ApiEnvelope<unknown>>(url, { withCredentials: true }),
    ).pipe(
      map((res) =>
        this.require(normalizeTrustedDeviceRevocation(this.data(res)), 'Trusted device removal'),
      ),
    );
  }

  private withCsrf<T>(request: () => Observable<T>): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(switchMap(request));
  }

  private data(res: ApiEnvelope<unknown>): unknown {
    return requireEnvelopeData(res, 'The sessions and devices response was incomplete.');
  }

  private require<T>(value: T | null, what: string): T {
    if (!value) {
      throw new Error(`${what} response was incomplete.`);
    }
    return value;
  }
}
