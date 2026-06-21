import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, inject, OnInit, PLATFORM_ID, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  AuthService,
  PendingOAuthSignupStorageKey,
  OAuthAuthResponse,
} from '../../services/auth.service';
import { environment } from '../../../../../environments/environment';
import {
  getApiClientMessage,
  getServerErrorMessage,
} from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';

@Component({
  selector: 'app-microsoft-callback',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './microsoft-callback.component.html',
  styleUrls: ['./microsoft-callback.component.css'],
})
export class MicrosoftCallbackComponent implements OnInit {
  private static readonly CodeVerifierStorageKey = 'ms_code_verifier';
  private static readonly StateStorageKey = 'ms_oauth_state';
  private static readonly NonceStorageKey = 'ms_oauth_nonce';
  private static readonly FallbackMessage =
    'Microsoft sign-in could not be completed. Please try again.';
  private platformId = inject(PLATFORM_ID);

  status = signal<'loading' | 'success' | 'error'>('loading');
  message = signal('Signing you in with Microsoft...');

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

    this.handleMicrosoftCallback();
  }

  async handleMicrosoftCallback(): Promise<void> {
    try {
      const params = new URLSearchParams(window.location.search);
      const code = params.get('code');
      const state = params.get('state');
      const expectedState = sessionStorage.getItem(MicrosoftCallbackComponent.StateStorageKey);
      const verifier = sessionStorage.getItem(MicrosoftCallbackComponent.CodeVerifierStorageKey);
      const nonce = sessionStorage.getItem(MicrosoftCallbackComponent.NonceStorageKey);

      if (!code || !state || !expectedState || state !== expectedState || !verifier || !nonce) {
        throw new Error('Microsoft sign-in could not be validated. Please try again.');
      }

      const tokenData = await this.exchangeCodeForIdToken(code, verifier);

      sessionStorage.removeItem(MicrosoftCallbackComponent.CodeVerifierStorageKey);
      sessionStorage.removeItem(MicrosoftCallbackComponent.StateStorageKey);
      sessionStorage.removeItem(MicrosoftCallbackComponent.NonceStorageKey);

      this.auth.microsoftVerify(tokenData.id_token, nonce).subscribe({
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
            const target = this.authReturnUrl.consume();
            setTimeout(() => this.router.navigateByUrl(target), 1500);
          } catch (err: any) {
            console.error(err);
            this.status.set('error');
            this.message.set(getApiClientMessage(err, 'Sign-in failed.'));
          }
        },
        error: (err) => {
          console.error(err);
          this.status.set('error');
          this.message.set(getApiClientMessage(err, 'Sign-in failed.'));
        },
      });
    } catch (err: any) {
      console.error(err);
      this.status.set('error');
      this.message.set(err.message || MicrosoftCallbackComponent.FallbackMessage);
    }
  }

  retry(): void {
    this.status.set('loading');
    this.message.set('Retrying sign-in...');
    this.handleMicrosoftCallback();
  }

  private async exchangeCodeForIdToken(
    code: string,
    verifier: string,
  ): Promise<{ id_token: string }> {
    const tokenUrl = 'https://login.microsoftonline.com/common/oauth2/v2.0/token';
    const data = new URLSearchParams({
      client_id: environment.msalClientId,
      grant_type: 'authorization_code',
      code,
      redirect_uri: `${environment.frontendUrl}/auth/microsoft`,
      code_verifier: verifier,
      scope: 'openid profile email offline_access',
    });

    let tokenResp: Response;
    try {
      tokenResp = await fetch(tokenUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: data.toString(),
      });
    } catch {
      throw new Error(getServerErrorMessage(0));
    }

    const tokenData = (await this.readJsonPayload(tokenResp)) as { id_token?: string } | null;
    if (!tokenResp.ok) {
      throw new Error(
        tokenResp.status >= 500
          ? getServerErrorMessage(tokenResp.status)
          : MicrosoftCallbackComponent.FallbackMessage,
      );
    }

    if (!tokenData?.id_token) {
      throw new Error(MicrosoftCallbackComponent.FallbackMessage);
    }

    return tokenData as { id_token: string };
  }

  private async readJsonPayload(response: Response): Promise<unknown> {
    const text = await response.text();
    if (!text) {
      return null;
    }

    try {
      return JSON.parse(text);
    } catch {
      return null;
    }
  }
}
