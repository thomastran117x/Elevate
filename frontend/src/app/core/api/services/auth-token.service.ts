import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Store } from '@ngrx/store';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { clearUser } from '../../stores/user.actions';
import { clearSession, updateSession } from '../../stores/session.actions';
import { SessionState } from '../../stores/session.reducer';
import { AuthenticatedSessionResponse } from '../../models/auth-response.model';
import { selectAccessToken } from '../../stores/session.selectors';

type CsrfResponse = { token: string };

@Injectable({ providedIn: 'root' })
export class AuthTokenService {
  accessToken: string | null = null;
  csrfToken: string | null = null;

  private csrfPromise: Promise<void> | null = null;

  constructor(
    private http: HttpClient,
    private store: Store<{ session: SessionState }>,
  ) {
    this.store
      .select(selectAccessToken)
      .subscribe((token) => {
        this.accessToken = token || null;
      });
  }

  async ensureCsrfToken(): Promise<void> {
    if (this.csrfToken) return;

    if (!this.csrfPromise) {
      this.csrfPromise = (async () => {
        const res = await firstValueFrom(
          this.http.get<CsrfResponse>(`${environment.backendUrl}/auth/csrf`, {
            withCredentials: true,
          }),
        );
        this.csrfToken = res?.token ?? null;
      })().finally(() => {
        this.csrfPromise = null;
      });
    }

    await this.csrfPromise;
  }

  async refreshAccessToken(): Promise<void> {
    await this.ensureCsrfToken();

    const res = await firstValueFrom(
      this.http.post<AuthenticatedSessionResponse>(
        `${environment.backendUrl}/auth/refresh`,
        {},
        {
          withCredentials: true,
        },
      ),
    );

    if (!res?.AccessToken) {
      throw new Error('Refresh response did not include an access token.');
    }

    this.store.dispatch(updateSession({
      accessToken: res.AccessToken,
      expiresAtUtc: res.ExpiresAtUtc,
    }));
    this.accessToken = res.AccessToken;
  }

  logoutLocal() {
    this.store.dispatch(clearUser());
    this.store.dispatch(clearSession());
    this.accessToken = null;
    this.csrfToken = null;
  }
}
