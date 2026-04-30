import { environment } from '../../../environments/environment';
import { Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Store } from '@ngrx/store';
import { setUser, clearUser } from '../stores/user.actions';
import { firstValueFrom } from 'rxjs';
import { AuthTokenService } from '../api/services/auth-token.service';
import { setSession, clearSession } from '../stores/session.actions';
import { AuthenticatedSessionResponse, CurrentUserResponse } from '../models/auth-response.model';
import { AuthService, ApiEnvelope } from '../../features/auth/services/auth.service';
import { UserState } from '../stores/user.reducer';
import { SessionState } from '../stores/session.reducer';

@Injectable({ providedIn: 'root' })
export class SessionManagerService {
  private http = inject(HttpClient);
  private store = inject(Store<{ user: UserState; session: SessionState }>);
  private auth = inject(AuthTokenService);
  private authService = inject(AuthService);
  private platformId = inject(PLATFORM_ID);

  loading = signal(isPlatformBrowser(this.platformId));

  async bootstrapSession(session: AuthenticatedSessionResponse): Promise<void> {
    if (!session?.AccessToken) {
      throw new Error('Authentication response did not include an access token.');
    }

    this.store.dispatch(setSession({
      session: {
        AccessToken: session.AccessToken,
        ExpiresAtUtc: session.ExpiresAtUtc,
      },
    }));

    try {
      const response = await firstValueFrom(this.authService.me());
      const user = this.requireCurrentUser(response);
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

    try {
      await this.auth.ensureCsrfToken();

      const headers = this.auth.csrfToken
        ? new HttpHeaders({ 'X-CSRF-TOKEN': this.auth.csrfToken })
        : undefined;

      const res = await firstValueFrom(
        this.http.post<AuthenticatedSessionResponse>(
          `${environment.backendUrl}/auth/refresh`,
          {},
          {
            withCredentials: true,
            headers,
          },
        ),
      );

      if (res?.AccessToken) {
        await this.bootstrapSession(res);
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

  private requireCurrentUser(response: ApiEnvelope<CurrentUserResponse>): CurrentUserResponse {
    const user = response.data ?? response.Data;
    if (!user) {
      throw new Error('Current user response was incomplete.');
    }

    return user;
  }

  private clearSessionState(): void {
    this.store.dispatch(clearUser());
    this.store.dispatch(clearSession());
  }
}
