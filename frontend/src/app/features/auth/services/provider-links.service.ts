import { Injectable } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';

import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import {
  asRecord,
  readBoolean,
  readNullableString,
  readString,
} from '../../../core/models/payload-casing';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { environment } from '../../../../environments/environment';

/** The providers an account can link. `apple` is reserved on the server and not offered yet. */
export type LinkableProvider = 'google' | 'microsoft';

export const LINKABLE_PROVIDERS: readonly LinkableProvider[] = ['google', 'microsoft'];

export const PROVIDER_LABELS: Record<LinkableProvider, string> = {
  google: 'Google',
  microsoft: 'Microsoft',
};

/** Error codes a provider link change is refused with. */
export const PROVIDER_LINK_EXPIRED_ERROR_CODE = 'PROVIDER_LINK_EXPIRED';
export const PROVIDER_LINK_MISMATCH_ERROR_CODE = 'PROVIDER_LINK_MISMATCH';
export const PROVIDER_ALREADY_LINKED_ERROR_CODE = 'PROVIDER_ALREADY_LINKED';
export const PROVIDER_ALREADY_CONNECTED_ERROR_CODE = 'PROVIDER_ALREADY_CONNECTED';
export const LAST_SIGN_IN_METHOD_ERROR_CODE = 'LAST_SIGN_IN_METHOD';

/** A linked identity. The provider's subject is never sent to the browser. */
export interface ProviderLink {
  provider: LinkableProvider;
  /** The address the provider last reported. Display only. */
  email: string | null;
  displayName: string | null;
  linkedAtUtc: string;
  /** Null until the link is first used to sign in. */
  lastUsedAtUtc: string | null;
}

export interface ProviderLinkStatus {
  provider: LinkableProvider;
  /** Whether this deployment can start a link with the provider. */
  available: boolean;
  link: ProviderLink | null;
  /** Whether removing the link leaves the account another way to sign in. */
  canUnlink: boolean;
}

export interface ProviderLinksOverview {
  hasPassword: boolean;
  providers: ProviderLinkStatus[];
}

export interface ProviderLinkStart {
  authorizationUrl: string;
  state: string;
  /** The exact redirect URI the authorization request names; a browser redemption must send it. */
  redirectUri: string;
  /** Only for a provider whose code the browser redeems itself (Microsoft). */
  codeVerifier: string | null;
}

/** What a provider callback returned for a link: a code for Google, an ID token for Microsoft. */
export interface ProviderLinkCompletion {
  state: string;
  code?: string;
  idToken?: string;
}

/** A link a provider sign-in proposed because its verified email matched an existing account. */
export interface PendingEmailLinkProposal {
  linkToken: string;
  provider: LinkableProvider;
  maskedEmail: string;
  expiresAtUtc: string;
}

export function isLinkableProvider(value: unknown): value is LinkableProvider {
  return value === 'google' || value === 'microsoft';
}

export function normalizeProviderLink(value: unknown): ProviderLink | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const provider = readString(source, 'Provider', 'provider');
  const linkedAtUtc = readString(source, 'LinkedAtUtc', 'linkedAtUtc');
  if (!isLinkableProvider(provider) || !linkedAtUtc) {
    return null;
  }

  return {
    provider,
    email: readNullableString(source, 'Email', 'email') ?? null,
    displayName: readNullableString(source, 'DisplayName', 'displayName') ?? null,
    linkedAtUtc,
    lastUsedAtUtc: readNullableString(source, 'LastUsedAtUtc', 'lastUsedAtUtc') ?? null,
  };
}

/** Accepts either casing; a provider the browser does not know is left out rather than failing. */
export function normalizeProviderLinksOverview(value: unknown): ProviderLinksOverview | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const items = source['Providers'] ?? source['providers'];
  const providers = (Array.isArray(items) ? items : [])
    .map((item): ProviderLinkStatus | null => {
      const entry = asRecord(item);
      const provider = entry ? readString(entry, 'Provider', 'provider') : undefined;
      if (!entry || !isLinkableProvider(provider)) {
        return null;
      }

      const link = normalizeProviderLink(entry['Link'] ?? entry['link']);
      return {
        provider,
        available: readBoolean(entry, 'Available', 'available') ?? false,
        link,
        canUnlink: !!link && (readBoolean(entry, 'CanUnlink', 'canUnlink') ?? false),
      };
    })
    .filter((status): status is ProviderLinkStatus => status !== null);

  return {
    hasPassword: readBoolean(source, 'HasPassword', 'hasPassword') ?? false,
    providers,
  };
}

export function normalizeEmailLinkProposal(value: unknown): PendingEmailLinkProposal | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const linkToken = readString(source, 'LinkToken', 'linkToken');
  const provider = readString(source, 'Provider', 'provider');
  const maskedEmail = readString(source, 'MaskedEmail', 'maskedEmail');
  const expiresAtUtc = readString(source, 'ExpiresAtUtc', 'expiresAtUtc');
  if (!linkToken || !isLinkableProvider(provider) || !maskedEmail || !expiresAtUtc) {
    return null;
  }

  return { linkToken, provider, maskedEmail, expiresAtUtc };
}

export function normalizeProviderLinkStart(value: unknown): ProviderLinkStart | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const authorizationUrl = readString(source, 'AuthorizationUrl', 'authorizationUrl');
  const state = readString(source, 'State', 'state');
  const redirectUri = readString(source, 'RedirectUri', 'redirectUri');
  if (!authorizationUrl || !state || !redirectUri) {
    return null;
  }

  return {
    authorizationUrl,
    state,
    redirectUri,
    codeVerifier: readNullableString(source, 'CodeVerifier', 'codeVerifier') ?? null,
  };
}

/** The signed-in owner's sign-in providers: list, link, confirm an email match, and unlink. */
@Injectable({ providedIn: 'root' })
export class ProviderLinksService {
  private readonly baseUrl = `${environment.backendUrl}/auth/providers`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
  ) {}

  getProviders(): Observable<ProviderLinksOverview> {
    return this.withCsrf(() =>
      this.api.get<ApiEnvelope<unknown>>(this.baseUrl, { withCredentials: true }),
    ).pipe(
      map((res) =>
        this.require(normalizeProviderLinksOverview(this.data(res)), 'Sign-in providers'),
      ),
    );
  }

  startLink(provider: LinkableProvider): Observable<ProviderLinkStart> {
    return this.withCsrf(() =>
      this.api.post<ApiEnvelope<unknown>>(
        `${this.baseUrl}/${provider}/link/start`,
        {},
        { withCredentials: true },
      ),
    ).pipe(map((res) => this.require(normalizeProviderLinkStart(this.data(res)), 'Link start')));
  }

  completeLink(
    provider: LinkableProvider,
    completion: ProviderLinkCompletion,
  ): Observable<ProviderLink> {
    return this.withCsrf(() =>
      this.api.post<ApiEnvelope<unknown>>(`${this.baseUrl}/${provider}/link/complete`, completion, {
        withCredentials: true,
      }),
    ).pipe(map((res) => this.require(normalizeProviderLink(this.data(res)), 'Link')));
  }

  confirmLink(linkToken: string): Observable<ProviderLink> {
    return this.withCsrf(() =>
      this.api.post<ApiEnvelope<unknown>>(
        `${this.baseUrl}/link/confirm`,
        { linkToken },
        { withCredentials: true },
      ),
    ).pipe(map((res) => this.require(normalizeProviderLink(this.data(res)), 'Link')));
  }

  unlink(provider: LinkableProvider): Observable<void> {
    return this.withCsrf(() =>
      this.api.delete<unknown>(`${this.baseUrl}/${provider}`, { withCredentials: true }),
    ).pipe(map(() => undefined));
  }

  private withCsrf<T>(request: () => Observable<T>): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(switchMap(request));
  }

  private data(res: ApiEnvelope<unknown>): unknown {
    return requireEnvelopeData(res, 'The sign-in provider response was incomplete.');
  }

  private require<T>(value: T | null, what: string): T {
    if (!value) {
      throw new Error(`${what} response was incomplete.`);
    }
    return value;
  }
}
