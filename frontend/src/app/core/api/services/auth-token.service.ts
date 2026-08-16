import { Injectable } from '@angular/core';
import { Store } from '@ngrx/store';
import { Observable, distinctUntilChanged, firstValueFrom, map } from 'rxjs';
import { ApiEnvelope, extractEnvelopeData } from '../models/api-envelope.model';
import { ApiClient } from './api-client.service';
import { environment } from '../../../../environments/environment';
import { clearUser } from '../../stores/user.actions';
import { clearSession, updateSession } from '../../stores/session.actions';
import { SessionState } from '../../stores/session.reducer';
import {
  AuthenticatedSessionResponse,
  normalizeAuthenticatedSessionResponse,
} from '../../models/auth-response.model';
import { selectAccessToken } from '../../stores/session.selectors';

type CsrfResponse = { token: string };

@Injectable({ providedIn: 'root' })
export class AuthTokenService {
  accessToken: string | null = null;
  csrfToken: string | null = null;

  /**
   * Emits the current access token whenever it changes, including a refresh that replaces an
   * expired token with a new one for the same user.
   *
   * Long-lived connections (the realtime hub) fix their principal at handshake time. A
   * reconnect that lands while the token is expired comes back anonymous, and the refresh
   * that follows would be invisible to them if this only reported signed-in versus anonymous
   * — so it reports the token itself, and consumers compare against what they connected with.
   */
  readonly accessToken$: Observable<string | null>;

  private csrfPromise: Promise<void> | null = null;

  constructor(
    private api: ApiClient,
    private store: Store<{ session: SessionState }>,
  ) {
    const token$ = this.store.select(selectAccessToken);

    token$.subscribe((token) => {
      this.accessToken = token || null;
    });

    this.accessToken$ = token$.pipe(
      map((token) => token || null),
      distinctUntilChanged(),
    );
  }

  async ensureCsrfToken(): Promise<void> {
    if (this.csrfToken) return;

    if (!this.csrfPromise) {
      this.csrfPromise = (async () => {
        const res = await firstValueFrom(
          this.api.get<ApiEnvelope<CsrfResponse> | CsrfResponse>(
            `${environment.backendUrl}/auth/csrf`,
            {
              withCredentials: true,
            },
          ),
        );
        const payload = extractEnvelopeData(res);
        this.csrfToken = payload?.token ?? null;
      })().finally(() => {
        this.csrfPromise = null;
      });
    }

    await this.csrfPromise;
  }

  async refreshAccessToken(): Promise<void> {
    await this.ensureCsrfToken();

    const res = await firstValueFrom(
      this.api.post<ApiEnvelope<AuthenticatedSessionResponse> | AuthenticatedSessionResponse>(
        `${environment.backendUrl}/auth/refresh`,
        {},
        {
          withCredentials: true,
        },
      ),
    );
    const payload = normalizeAuthenticatedSessionResponse(extractEnvelopeData(res));

    if (!payload?.AccessToken) {
      throw new Error('Refresh response did not include an access token.');
    }

    this.store.dispatch(
      updateSession({
        accessToken: payload.AccessToken,
        expiresAtUtc: payload.ExpiresAtUtc,
      }),
    );
    this.accessToken = payload.AccessToken;
  }

  logoutLocal() {
    this.store.dispatch(clearUser());
    this.store.dispatch(clearSession());
    this.accessToken = null;
    this.csrfToken = null;
  }
}
