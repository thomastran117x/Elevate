import { environment } from '../../../environments/environment';
import { Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Store } from '@ngrx/store';
import { setUser, clearUser } from '../stores/user.actions';
import { firstValueFrom } from 'rxjs';
import { ApiEnvelope, extractEnvelopeData } from '../api/models/api-envelope.model';
import { AuthTokenService } from '../api/services/auth-token.service';
import { setSession, clearSession } from '../stores/session.actions';
import {
  AuthenticatedSessionResponse,
  normalizeAuthenticatedSessionResponse,
} from '../models/auth-response.model';
import { AuthService } from '../../features/auth/services/auth.service';
import { UserState } from '../stores/user.reducer';
import { SessionState } from '../stores/session.reducer';
import { FeatureFlagsService } from '../features/feature-flags.service';
import { FEATURE_KEYS } from '../features/feature-flags.types';

@Injectable({ providedIn: 'root' })
export class SessionManagerService {
  private http = inject(HttpClient);
  private store = inject(Store<{ user: UserState; session: SessionState }>);
  private auth = inject(AuthTokenService);
  private authService = inject(AuthService);
  private platformId = inject(PLATFORM_ID);
  private featureFlags = inject(FeatureFlagsService);

  loading = signal(isPlatformBrowser(this.platformId));

  async bootstrapSession(session: AuthenticatedSessionResponse | unknown): Promise<void> {
    const normalizedSession = normalizeAuthenticatedSessionResponse(session);
    if (!normalizedSession?.AccessToken) {
      throw new Error('Authentication response did not include an access token.');
    }

    this.store.dispatch(
      setSession({
        session: {
          AccessToken: normalizedSession.AccessToken,
          ExpiresAtUtc: normalizedSession.ExpiresAtUtc,
        },
      }),
    );

    try {
      const user = await firstValueFrom(this.authService.me());
      this.store.dispatch(setUser({ user }));
    } catch (error) {
      this.clearSessionState();
      throw error;
    }
  }

  async restoreSession(): Promise<void> {
    if (!isPlatformBrowser(this.platformId)) {
      this.loading.set(false);
      return;
    }

    if (!this.featureFlags.isEnabled(FEATURE_KEYS.auth)) {
      this.clearSessionState();
      this.loading.set(false);
      return;
    }

    try {
      await this.auth.ensureCsrfToken();

      const headers = this.auth.csrfToken
        ? new HttpHeaders({ 'X-CSRF-TOKEN': this.auth.csrfToken })
        : undefined;

      const res = await firstValueFrom(
        this.http.post<ApiEnvelope<AuthenticatedSessionResponse> | AuthenticatedSessionResponse>(
          `${environment.backendUrl}/auth/refresh`,
          {},
          {
            withCredentials: true,
            headers,
          },
        ),
      );
      const session = normalizeAuthenticatedSessionResponse(extractEnvelopeData(res));

      if (session?.AccessToken) {
        await this.bootstrapSession(session);
      } else {
        this.clearSessionState();
      }
    } catch (err) {
      console.warn('Session restore failed:', err);
      this.clearSessionState();
    } finally {
      this.loading.set(false);
    }
  }

  private clearSessionState(): void {
    this.store.dispatch(clearUser());
    this.store.dispatch(clearSession());
  }
}
