import { environment } from '../../../environments/environment';
import { Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Store } from '@ngrx/store';
import { setUser, clearUser } from '../stores/user.actions';
import { AuthResponse } from '../models/auth-response.model';
import { firstValueFrom } from 'rxjs';
import { AuthTokenService } from '../api/services/auth-token.service';

@Injectable({ providedIn: 'root' })
export class SessionManagerService {
  private http = inject(HttpClient);
  private store = inject(Store);
  private auth = inject(AuthTokenService);
  private platformId = inject(PLATFORM_ID);

  loading = signal(isPlatformBrowser(this.platformId));

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
        this.http.post<AuthResponse>(
          `${environment.backendUrl}/auth/refresh`,
          {},
          {
            withCredentials: true,
            headers,
          },
        ),
      );

      const token = res?.Token ?? res?.AccessToken ?? null;

      if (token) {
        this.store.dispatch(setUser({ user: { ...res, Token: token } }));
      } else {
        this.store.dispatch(clearUser());
      }
    } catch (err) {
      console.warn('Session restore failed:', err);
      this.store.dispatch(clearUser());
    } finally {
      this.loading.set(false);
    }
  }
}
