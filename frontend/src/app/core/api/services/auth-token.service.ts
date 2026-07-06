import { Injectable } from '@angular/core';
import { Store } from '@ngrx/store';
import { firstValueFrom } from 'rxjs';
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

  private csrfPromise: Promise<void> | null = null;

  constructor(
    private api: ApiClient,
    private store: Store<{ session: SessionState }>,
  ) {
    this.store.select(selectAccessToken).subscribe((token) => {
      const next = token || null;

      // ASP.NET antiforgery tokens are bound to the current user's identity. When we cross
      // the anonymous <-> authenticated boundary (login/logout), a CSRF token fetched under
      // the previous identity is no longer valid — invalidate it so the next state-changing
      // request fetches a fresh, identity-matched token instead of failing validation.
      const identityChanged = (next === null) !== (this.accessToken === null);
      if (identityChanged) {
        this.csrfToken = null;
      }

      this.accessToken = next;
    });
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
