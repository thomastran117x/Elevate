import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, inject, OnInit, PLATFORM_ID, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { environment } from '../../../../../environments/environment';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import {
  AuthService,
  OAuthAuthenticationResponse,
  PendingLoginStepUpStorageKey,
  PendingOAuthSignupStorageKey,
} from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';

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
    private authReturnUrl: AuthReturnUrlService,
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
      sessionStorage.removeItem(GoogleCallbackComponent.CodeVerifierStorageKey);
      sessionStorage.removeItem(GoogleCallbackComponent.StateStorageKey);
      sessionStorage.removeItem(GoogleCallbackComponent.NonceStorageKey);

      this.auth
        .googleCodeVerify(
          code,
          codeVerifier,
          `${environment.frontendUrl}/auth/google`,
          nonce,
          this.authReturnUrl.peek() ?? undefined,
        )
        .subscribe({
          next: async (res: OAuthAuthenticationResponse) => {
            if (res.Type === 'requires_role_selection') {
              sessionStorage.setItem(
                PendingOAuthSignupStorageKey,
                JSON.stringify(res.RoleSelection),
              );
              this.status.set('success');
              this.message.set('Choose your role to finish creating your account...');
              setTimeout(() => this.router.navigate(['/auth/oauth/role']), 250);
              return;
            }

            if (res.Type === 'requires_step_up') {
              sessionStorage.setItem(PendingLoginStepUpStorageKey, JSON.stringify(res.StepUp));
              this.status.set('success');
              this.message.set('One more sign-in check is needed. Redirecting...');
              setTimeout(() => this.router.navigate(['/auth/mfa']), 250);
              return;
            }

            try {
              await this.sessionManager.bootstrapSession(res.Auth);
              this.status.set('success');
              this.message.set('Login successful! Redirecting...');
              const target = this.authReturnUrl.consume(res.Auth.ReturnPath ?? '/dashboard');
              setTimeout(() => this.router.navigateByUrl(target), 1500);
            } catch (err: any) {
              console.error('Google session bootstrap failed:', err);
              this.status.set('error');
              this.message.set(
                getApiClientMessage(err, 'Google sign-in failed. Please try again.'),
              );
            }
          },
          error: (err) => {
            console.error('Google callback failed:', err);
            this.status.set('error');
            this.message.set(getApiClientMessage(err, 'Google sign-in failed. Please try again.'));
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
