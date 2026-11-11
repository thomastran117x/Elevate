import { Injectable, Injector, inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { Observable, filter, firstValueFrom, map, tap } from 'rxjs';

import {
  isApiClientErrorCode,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { SessionManagerService } from '../../../core/services/session-manager.service';
import { AuthReturnUrlService } from './auth-return-url.service';
import { MicrosoftTokenExchangeService } from './microsoft-token-exchange.service';
import {
  isLinkableProvider,
  LinkableProvider,
  PROVIDER_ALREADY_CONNECTED_ERROR_CODE,
  PROVIDER_ALREADY_LINKED_ERROR_CODE,
  PROVIDER_LINK_EXPIRED_ERROR_CODE,
  PROVIDER_LINK_MISMATCH_ERROR_CODE,
  PendingEmailLinkProposal,
  ProviderLinksService,
} from './provider-links.service';

/** Holds the link a security-settings flow started, until the provider sends the browser back. */
export const PendingProviderLinkStorageKey = 'oauth_link_pending';

/** Holds a link a provider sign-in proposed, until the owner signs in and confirms it. */
export const PendingEmailLinkStorageKey = 'pending_provider_link';

/** Where a finished link flow lands, and the query parameters that say how it went. */
export const PROVIDER_LINK_RETURN_PATH = '/account/security';
export const PROVIDER_LINK_RESULT_PARAM = 'providerLink';
export const PROVIDER_LINK_PROVIDER_PARAM = 'provider';

/** A started link outlives neither the server's state nor the step-up proof it needs. */
const LINK_LIFETIME_MS = 10 * 60 * 1000;

/** Microsoft linking authorizes no refresh token, so redeeming its code asks for none. */
const MICROSOFT_LINK_SCOPE = 'openid profile email';

export type ProviderLinkOutcome =
  | 'linked'
  | 'cancelled'
  | 'expired'
  | 'already_linked'
  | 'already_connected'
  | 'mismatch'
  | 'step_up'
  | 'rejected'
  | 'signed_out'
  | 'failed';

export const PROVIDER_LINK_OUTCOMES: readonly ProviderLinkOutcome[] = [
  'linked',
  'cancelled',
  'expired',
  'already_linked',
  'already_connected',
  'mismatch',
  'step_up',
  'rejected',
  'signed_out',
  'failed',
];

interface PendingProviderLink {
  provider: LinkableProvider;
  state: string;
  redirectUri: string | null;
  codeVerifier: string | null;
  startedAt: number;
}

/**
 * Runs the browser half of explicit provider linking. The server issues and keeps the state,
 * nonce, and PKCE verifier; this only remembers which state it is waiting for, so the shared
 * provider callback pages can tell a returning link from a sign-in.
 */
@Injectable({ providedIn: 'root' })
export class ProviderLinkFlowService {
  private readonly providerLinks = inject(ProviderLinksService);
  private readonly microsoftExchange = inject(MicrosoftTokenExchangeService);
  private readonly session = inject(SessionManagerService);
  private readonly authToken = inject(AuthTokenService);
  private readonly authReturnUrl = inject(AuthReturnUrlService);
  private readonly injector = inject(Injector);

  /** Asks the server to start a link, then sends the browser to the provider. */
  begin(provider: LinkableProvider): Observable<void> {
    return this.providerLinks.startLink(provider).pipe(
      tap((start) => {
        write(PendingProviderLinkStorageKey, {
          provider,
          state: start.state,
          redirectUri: start.redirectUri,
          codeVerifier: start.codeVerifier,
          startedAt: Date.now(),
        } satisfies PendingProviderLink);
        this.redirect(start.authorizationUrl);
      }),
      map(() => undefined),
    );
  }

  /** Whether this callback is the return of a link this browser started for the provider. */
  isLinkCallback(provider: LinkableProvider, search: string): boolean {
    const pending = this.readPendingLink();
    const state = new URLSearchParams(search).get('state');
    return !!pending && pending.provider === provider && !!state && pending.state === state;
  }

  /** Finishes a returning link and gives the in-app URL that reports how it went. */
  async completeFromCallback(provider: LinkableProvider, search: string): Promise<string> {
    const pending = this.readPendingLink();
    remove(PendingProviderLinkStorageKey);
    const params = new URLSearchParams(search);

    if (params.get('error')) {
      return resultUrl(params.get('error') === 'access_denied' ? 'cancelled' : 'failed', provider);
    }

    const code = params.get('code');
    const state = params.get('state');
    if (!pending || pending.provider !== provider || !state || pending.state !== state || !code) {
      return resultUrl('failed', provider);
    }

    if (Date.now() - pending.startedAt > LINK_LIFETIME_MS) {
      return resultUrl('expired', provider);
    }

    // A provider redirect is a full page load, so the session may still be restoring.
    await this.waitForSessionRestore();
    if (!this.authToken.accessToken) {
      return resultUrl('signed_out', provider);
    }

    try {
      if (provider === 'microsoft') {
        if (!pending.codeVerifier || !pending.redirectUri) {
          return resultUrl('failed', provider);
        }
        // Redeemed with the redirect the server put in the authorization request, never one
        // rebuilt from this app's configuration, which Microsoft would refuse on any mismatch.
        const idToken = await this.microsoftExchange.exchange(code, pending.codeVerifier, {
          scope: MICROSOFT_LINK_SCOPE,
          redirectUri: pending.redirectUri,
        });
        await firstValueFrom(this.providerLinks.completeLink(provider, { state, idToken }));
      } else {
        await firstValueFrom(this.providerLinks.completeLink(provider, { state, code }));
      }
      return resultUrl('linked', provider);
    } catch (err) {
      return resultUrl(outcomeOf(err), provider);
    }
  }

  /**
   * Keeps a proposal for its owner and sends the next sign-in to the page that confirms it.
   * Where the user was headed is kept with the proposal, so declining it restores that.
   */
  proposeEmailLink(proposal: PendingEmailLinkProposal): void {
    const returnUrl = attempt(() => this.authReturnUrl.peek());
    write(PendingEmailLinkStorageKey, { ...proposal, returnUrl });
    attempt(() => this.authReturnUrl.set(PROVIDER_LINK_RETURN_PATH));
  }

  /** Forgets the proposal before signing in, and restores where the user was headed. */
  declineEmailProposal(): void {
    const stored = read(PendingEmailLinkStorageKey);
    const returnUrl = typeof stored?.['returnUrl'] === 'string' ? stored['returnUrl'] : null;
    remove(PendingEmailLinkStorageKey);

    attempt(() => {
      if (this.authReturnUrl.peek() === PROVIDER_LINK_RETURN_PATH) {
        this.authReturnUrl.consume();
      }
      this.authReturnUrl.set(returnUrl);
    });
  }

  /** The proposal still waiting for its owner, or null once it expired or was used. */
  readEmailProposal(): PendingEmailLinkProposal | null {
    const value = read(PendingEmailLinkStorageKey);
    if (
      !value ||
      typeof value['linkToken'] !== 'string' ||
      !isLinkableProvider(value['provider']) ||
      typeof value['maskedEmail'] !== 'string' ||
      typeof value['expiresAtUtc'] !== 'string'
    ) {
      return null;
    }

    const expiresAt = Date.parse(value['expiresAtUtc']);
    if (Number.isNaN(expiresAt) || expiresAt <= Date.now()) {
      remove(PendingEmailLinkStorageKey);
      return null;
    }

    return {
      linkToken: value['linkToken'],
      provider: value['provider'],
      maskedEmail: value['maskedEmail'],
      expiresAtUtc: value['expiresAtUtc'],
    };
  }

  clearEmailProposal(): void {
    remove(PendingEmailLinkStorageKey);
  }

  /** A full-page navigation; overridden in tests. */
  protected redirect(url: string): void {
    window.location.assign(url);
  }

  private readPendingLink(): PendingProviderLink | null {
    const value = read(PendingProviderLinkStorageKey);
    if (
      !value ||
      !isLinkableProvider(value['provider']) ||
      typeof value['state'] !== 'string' ||
      typeof value['startedAt'] !== 'number'
    ) {
      return null;
    }

    return {
      provider: value['provider'],
      state: value['state'],
      redirectUri: typeof value['redirectUri'] === 'string' ? value['redirectUri'] : null,
      codeVerifier: typeof value['codeVerifier'] === 'string' ? value['codeVerifier'] : null,
      startedAt: value['startedAt'],
    };
  }

  private async waitForSessionRestore(): Promise<void> {
    if (!this.session.loading()) {
      return;
    }

    await firstValueFrom(
      toObservable(this.session.loading, { injector: this.injector }).pipe(
        filter((loading) => !loading),
      ),
    );
  }
}

function outcomeOf(err: unknown): ProviderLinkOutcome {
  if (isApiClientErrorCode(err, PROVIDER_LINK_EXPIRED_ERROR_CODE)) return 'expired';
  if (isApiClientErrorCode(err, PROVIDER_ALREADY_LINKED_ERROR_CODE)) return 'already_linked';
  if (isApiClientErrorCode(err, PROVIDER_ALREADY_CONNECTED_ERROR_CODE)) return 'already_connected';
  if (isApiClientErrorCode(err, PROVIDER_LINK_MISMATCH_ERROR_CODE)) return 'mismatch';
  if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) return 'step_up';

  const status = (err as { status?: unknown } | null)?.status;
  return status === 400 ? 'rejected' : 'failed';
}

function resultUrl(outcome: ProviderLinkOutcome, provider: LinkableProvider): string {
  const query = new URLSearchParams({
    [PROVIDER_LINK_RESULT_PARAM]: outcome,
    [PROVIDER_LINK_PROVIDER_PARAM]: provider,
  });
  return `${PROVIDER_LINK_RETURN_PATH}?${query.toString()}`;
}

/** Runs a storage-backed step that a blocked storage (a private window) may refuse. */
function attempt<T>(step: () => T): T | null {
  try {
    return step();
  } catch {
    return null;
  }
}

function read(key: string): Record<string, unknown> | null {
  if (typeof sessionStorage === 'undefined') {
    return null;
  }

  try {
    const raw = sessionStorage.getItem(key);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

function write(key: string, value: object): void {
  if (typeof sessionStorage === 'undefined') {
    return;
  }

  try {
    sessionStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Storage can be full or blocked; the flow then reports a failure on return.
  }
}

function remove(key: string): void {
  if (typeof sessionStorage === 'undefined') {
    return;
  }

  try {
    sessionStorage.removeItem(key);
  } catch {
    // Nothing to clean up if storage is unavailable.
  }
}
