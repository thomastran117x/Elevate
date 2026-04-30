import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, inject, OnInit, PLATFORM_ID, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  AuthService,
  PendingOAuthSignupStorageKey,
  OAuthAuthResponse,
} from '../../services/auth.service';
import { environment } from '../../../../../environments/environment';
import { SessionManagerService } from '../../../../core/services/session-manager.service';

@Component({
  selector: 'app-google-callback',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './google-callback.component.html',
  styleUrls: ['./google-callback.component.css'],
})
export class GoogleCallbackComponent implements OnInit {
  private static readonly CodeVerifierStorageKey = 'google_code_verifier';
  private static readonly StateStorageKey = 'google_oauth_state';
  private static readonly NonceStorageKey = 'google_oauth_nonce';

  private platformId = inject(PLATFORM_ID);

  status = signal<'loading' | 'success' | 'error'>('loading');
  message = signal('Completing Google sign-in...');

  constructor(
    private auth: AuthService,
    private sessionManager: SessionManagerService,
    private router: Router,
  ) {}

  ngOnInit(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    void this.handleGoogleCallback();
  }

  private async handleGoogleCallback(): Promise<void> {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    const expectedState = sessionStorage.getItem(GoogleCallbackComponent.StateStorageKey);
    const codeVerifier = sessionStorage.getItem(GoogleCallbackComponent.CodeVerifierStorageKey);
    const nonce = sessionStorage.getItem(GoogleCallbackComponent.NonceStorageKey);

    if (!code || !state || !expectedState || state !== expectedState || !codeVerifier || !nonce) {
      this.status.set('error');
      this.message.set('Google sign-in could not be validated. Please try again.');
      return;
    }

    this.status.set('loading');
    this.message.set('Verifying Google token...');

    try {
      const tokenResp = await fetch('https://oauth2.googleapis.com/token', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          client_id: environment.googleClientId,
          grant_type: 'authorization_code',
          code,
          redirect_uri: `${environment.frontendUrl}/auth/google`,
          code_verifier: codeVerifier,
        }).toString(),
      });

      const tokenData = await tokenResp.json();
      if (!tokenResp.ok || !tokenData.id_token) {
        throw new Error('No id_token returned from Google.');
      }

      sessionStorage.removeItem(GoogleCallbackComponent.CodeVerifierStorageKey);
      sessionStorage.removeItem(GoogleCallbackComponent.StateStorageKey);
      sessionStorage.removeItem(GoogleCallbackComponent.NonceStorageKey);

      this.auth.googleVerify(tokenData.id_token, nonce).subscribe({
        next: async (res: OAuthAuthResponse) => {
          if (res.RequiresRoleSelection) {
            sessionStorage.setItem(PendingOAuthSignupStorageKey, JSON.stringify(res));
            this.status.set('success');
            this.message.set('Choose your role to finish creating your account...');
            setTimeout(() => this.router.navigate(['/auth/oauth/role']), 250);
            return;
          }

          try {
            await this.sessionManager.bootstrapSession(res.Auth);
            this.status.set('success');
            this.message.set('Login successful! Redirecting...');
            setTimeout(() => this.router.navigate(['/dashboard']), 1500);
          } catch (err: any) {
            console.error('Google session bootstrap failed:', err);
            this.status.set('error');
            this.message.set(err?.error?.message || err?.message || 'Google sign-in failed. Please try again.');
          }
        },
        error: (err) => {
          console.error('Google callback failed:', err);
          this.status.set('error');
          this.message.set(err?.error?.message || 'Google sign-in failed. Please try again.');
        },
      });
    } catch (err) {
      console.error('Google callback failed:', err);
      this.status.set('error');
      this.message.set('Google sign-in failed. Please try again.');
    }
  }

  retry(): void {
    this.status.set('loading');
    this.message.set('Retrying Google sign-in...');
    void this.handleGoogleCallback();
  }
}
