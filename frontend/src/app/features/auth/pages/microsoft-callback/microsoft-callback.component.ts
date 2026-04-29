import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, inject, OnInit, PLATFORM_ID, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import {
  AuthService,
  PendingOAuthSignupStorageKey,
  OAuthAuthResponse,
} from '../../services/auth.service';
import { setUser } from '../../../../core/stores/user.actions';
import { UserState } from '../../../../core/stores/user.reducer';
import { environment } from '../../../../../environments/environment';

@Component({
  selector: 'app-microsoft-callback',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './microsoft-callback.component.html',
  styleUrls: ['./microsoft-callback.component.css'],
})
export class MicrosoftCallbackComponent implements OnInit {
  private platformId = inject(PLATFORM_ID);

  status = signal<'loading' | 'success' | 'error'>('loading');
  message = signal('Signing you in with Microsoft...');

  constructor(
    private auth: AuthService,
    private store: Store<{ user: UserState }>,
    private router: Router,
  ) {}

  ngOnInit(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    this.handleMicrosoftCallback();
  }

  async handleMicrosoftCallback(): Promise<void> {
    try {
      const params = new URLSearchParams(window.location.search);
      const code = params.get('code');
      const verifier = sessionStorage.getItem('ms_code_verifier');

      if (!code || !verifier) throw new Error('Missing code or verifier');

      const tokenUrl = 'https://login.microsoftonline.com/common/oauth2/v2.0/token';
      const data = new URLSearchParams({
        client_id: environment.msalClientId,
        grant_type: 'authorization_code',
        code,
        redirect_uri: `${environment.frontendUrl}/auth/microsoft`,
        code_verifier: verifier,
        scope: 'openid profile email offline_access',
      });

      const tokenResp = await fetch(tokenUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: data.toString(),
      });

      const tokenData = await tokenResp.json();
      if (!tokenData.id_token) throw new Error('No id_token returned from Microsoft.');

      this.auth.microsoftVerify(tokenData.id_token).subscribe({
        next: (res: OAuthAuthResponse) => {
          if (res.RequiresRoleSelection) {
            sessionStorage.setItem(PendingOAuthSignupStorageKey, JSON.stringify(res));
            this.status.set('success');
            this.message.set('Choose your role to finish creating your account...');
            setTimeout(() => this.router.navigate(['/auth/oauth/role']), 250);
            return;
          }

          this.store.dispatch(setUser({ user: res.Auth }));
          this.status.set('success');
          this.message.set('Login successful! Redirecting...');
          setTimeout(() => this.router.navigate(['/dashboard']), 1500);
        },
        error: (err) => {
          console.error(err);
          this.status.set('error');
          this.message.set(err?.error?.message || 'Sign-in failed.');
        },
      });
    } catch (err: any) {
      console.error(err);
      this.status.set('error');
      this.message.set(err.message || 'Microsoft sign-in failed.');
    }
  }

  retry(): void {
    this.status.set('loading');
    this.message.set('Retrying sign-in...');
    this.handleMicrosoftCallback();
  }
}
