import { Injectable } from '@angular/core';
import { Observable, from, map, of, switchMap } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  AuthenticatedSessionResponse,
  normalizeAuthenticatedSessionResponse,
} from '../../../core/models/auth-response.model';
import {
  UnknownRecord,
  asRecord,
  readBoolean,
  readNullableString,
  readNumber,
  readString,
} from '../../../core/models/payload-casing';
import { WebAuthnFailureReason, WebAuthnService } from '../../../core/webauthn/webauthn.service';

/** One of the signed-in user's passkeys. Credential ids and keys never reach the browser. */
export interface Passkey {
  id: number;
  name: string;
  /** The passkey can sync between devices, e.g. through a password manager. */
  syncable: boolean;
  backedUp: boolean;
  /** False when it is the account's last way to sign in. */
  canRemove: boolean;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface PasskeyList {
  passkeys: Passkey[];
  /** How many passkeys one account may register. */
  limit: number;
}

/** The options for one ceremony, and the single-use id its completion sends back. */
export interface PasskeyCeremony {
  ceremonyId: string;
  /** WebAuthn options JSON, passed to the browser as is. */
  publicKey: UnknownRecord;
}

/**
 * How a passkey flow ended without a server answer. A server refusal is an observable error
 * instead, so callers keep handling `MFA_REQUIRED` and friends as they do elsewhere.
 */
export type PasskeyOutcome<T> =
  { status: 'done'; value: T } | { status: WebAuthnFailureReason; value?: undefined };

/** Error codes a passkey request can answer with. */
export const PASSKEY_LIMIT_REACHED_CODE = 'PASSKEY_LIMIT_REACHED';
export const PASSKEY_AUTHENTICATION_FAILED_CODE = 'AUTHENTICATION_FAILED';

export function normalizePasskey(value: unknown): Passkey | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const id = readNumber(source, 'Id', 'id');
  const name = readString(source, 'Name', 'name');
  const createdAt = readString(source, 'CreatedAt', 'createdAt');
  if (id === undefined || !name || !createdAt) {
    return null;
  }

  return {
    id,
    name,
    syncable: readBoolean(source, 'Syncable', 'syncable') ?? false,
    backedUp: readBoolean(source, 'BackedUp', 'backedUp') ?? false,
    canRemove: readBoolean(source, 'CanRemove', 'canRemove') ?? false,
    createdAt,
    lastUsedAt: readNullableString(source, 'LastUsedAt', 'lastUsedAt') ?? null,
  };
}

/** Accepts either casing and drops rows missing what a row needs, so one cannot blank the list. */
export function normalizePasskeyList(value: unknown): PasskeyList {
  const source = asRecord(value);
  const items = source ? (source['Passkeys'] ?? source['passkeys']) : undefined;
  return {
    passkeys: (Array.isArray(items) ? items : [])
      .map(normalizePasskey)
      .filter((passkey): passkey is Passkey => passkey !== null),
    limit: (source && readNumber(source, 'Limit', 'limit')) ?? 0,
  };
}

export function normalizePasskeyCeremony(value: unknown): PasskeyCeremony | null {
  const source = asRecord(value);
  const ceremonyId = source ? readString(source, 'CeremonyId', 'ceremonyId') : undefined;
  const publicKey = source ? asRecord(source['PublicKey'] ?? source['publicKey']) : null;
  return ceremonyId && publicKey ? { ceremonyId, publicKey } : null;
}

/**
 * Passkeys: managing the signed-in user's, signing in with one, and stepping up with one.
 * Each ceremony asks the API for options, runs the browser prompt, and sends the result back.
 * Management calls need a fresh step-up and fail with `MFA_REQUIRED` without one.
 */
@Injectable({ providedIn: 'root' })
export class PasskeysService {
  private readonly baseUrl = `${environment.backendUrl}/auth/passkeys`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
    private webAuthn: WebAuthnService,
  ) {}

  /** Whether this browser can create and use passkeys at all. False during server rendering. */
  isSupported(): boolean {
    return this.webAuthn.isSupported();
  }

  isConditionalMediationAvailable(): Promise<boolean> {
    return this.webAuthn.isConditionalMediationAvailable();
  }

  list(): Observable<PasskeyList> {
    return this.withCsrf(() =>
      this.api.get<ApiEnvelope<unknown>>(this.baseUrl, { withCredentials: true }),
    ).pipe(map((res) => normalizePasskeyList(this.data(res))));
  }

  /** Creates a passkey on this device and registers it under {@link name}. */
  register(name: string | null): Observable<PasskeyOutcome<Passkey>> {
    return this.post(`${this.baseUrl}/registration/options`, { name: name || null }).pipe(
      map((data) => this.requireCeremony(data)),
      switchMap((ceremony) =>
        from(this.webAuthn.create(ceremony.publicKey)).pipe(
          switchMap((result) =>
            result.ok
              ? this.post(`${this.baseUrl}/registration`, {
                  ceremonyId: ceremony.ceremonyId,
                  credential: result.credential,
                }).pipe(
                  map((data): PasskeyOutcome<Passkey> => ({
                    status: 'done',
                    value: this.require(normalizePasskey(data), 'Passkey registration'),
                  })),
                )
              : of<PasskeyOutcome<Passkey>>({ status: result.reason }),
          ),
        ),
      ),
    );
  }

  rename(passkeyId: number, name: string): Observable<Passkey> {
    return this.withCsrf(() =>
      this.api.patch<ApiEnvelope<unknown>>(
        `${this.baseUrl}/${passkeyId}`,
        { name },
        { withCredentials: true },
      ),
    ).pipe(map((res) => this.require(normalizePasskey(this.data(res)), 'Passkey rename')));
  }

  remove(passkeyId: number): Observable<void> {
    return this.withCsrf(() =>
      this.api.delete<ApiEnvelope<unknown>>(`${this.baseUrl}/${passkeyId}`, {
        withCredentials: true,
      }),
    ).pipe(map(() => undefined));
  }

  /**
   * Signs in with any passkey this device holds for the site. With `mediation: 'conditional'`
   * the prompt waits in the username field's autofill until the user picks a passkey there, or
   * until {@link signal} aborts it.
   */
  signIn(
    rememberMe: boolean,
    options: { mediation?: CredentialMediationRequirement; signal?: AbortSignal } = {},
  ): Observable<PasskeyOutcome<AuthenticatedSessionResponse>> {
    return this.post(`${this.baseUrl}/sign-in/options`, {}).pipe(
      map((data) => this.requireCeremony(data)),
      switchMap((ceremony) =>
        from(this.webAuthn.get(ceremony.publicKey, options)).pipe(
          switchMap((result) =>
            result.ok
              ? this.post(`${this.baseUrl}/sign-in`, {
                  ceremonyId: ceremony.ceremonyId,
                  credential: result.credential,
                  rememberMe,
                  transport: 'browser',
                }).pipe(
                  map((data): PasskeyOutcome<AuthenticatedSessionResponse> => {
                    const source = asRecord(data);
                    const auth = normalizeAuthenticatedSessionResponse(
                      source?.['Auth'] ?? source?.['auth'],
                    );
                    return { status: 'done', value: this.require(auth, 'Passkey sign-in') };
                  }),
                )
              : of<PasskeyOutcome<AuthenticatedSessionResponse>>({ status: result.reason }),
          ),
        ),
      ),
    );
  }

  /** Verifies this session with one of the user's passkeys, satisfying the step-up gate. */
  stepUp(): Observable<PasskeyOutcome<void>> {
    return this.post(`${this.baseUrl}/step-up/options`, {}).pipe(
      map((data) => this.requireCeremony(data)),
      switchMap((ceremony) =>
        from(this.webAuthn.get(ceremony.publicKey)).pipe(
          switchMap((result) =>
            result.ok
              ? this.withCsrf(() =>
                  // The proof is the whole answer, so the envelope carries no data.
                  this.api.post<ApiEnvelope<unknown>>(
                    `${this.baseUrl}/step-up`,
                    { ceremonyId: ceremony.ceremonyId, credential: result.credential },
                    { withCredentials: true },
                  ),
                ).pipe(map((): PasskeyOutcome<void> => ({ status: 'done', value: undefined })))
              : of<PasskeyOutcome<void>>({ status: result.reason }),
          ),
        ),
      ),
    );
  }

  private post(url: string, body: unknown): Observable<unknown> {
    return this.withCsrf(() =>
      this.api.post<ApiEnvelope<unknown>>(url, body, { withCredentials: true }),
    ).pipe(map((res) => this.data(res)));
  }

  private withCsrf<T>(request: () => Observable<T>): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(switchMap(request));
  }

  private data(res: ApiEnvelope<unknown>): unknown {
    return requireEnvelopeData(res, 'The passkey response was incomplete.');
  }

  private requireCeremony(data: unknown): PasskeyCeremony {
    return this.require(normalizePasskeyCeremony(data), 'Passkey options');
  }

  private require<T>(value: T | null, what: string): T {
    if (value === null) {
      throw new Error(`${what} response was incomplete.`);
    }
    return value;
  }
}
