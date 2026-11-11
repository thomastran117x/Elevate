import { isPlatformBrowser } from '@angular/common';
import {
  Component,
  inject,
  OnInit,
  PLATFORM_ID,
  signal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthTokenService } from '../../../../core/api/services/auth-token.service';
import {
  DEVICE_VERIFICATION_REQUIRED_ERROR_CODE,
  DEVICE_VERIFICATION_REQUIRED_MESSAGE,
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import {
  AuthService,
  OAuthAuthenticationResponse,
  PendingLoginStepUpStorageKey,
  PendingOAuthSignupStorageKey,
} from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import {
  MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE,
  MicrosoftTokenExchangeService,
} from '../../services/microsoft-token-exchange.service';
import { ProviderLinkFlowService } from '../../services/provider-link-flow.service';

@Component({
  selector: 'app-microsoft-callback',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './microsoft-callback.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./microsoft-callback.component.css'],
})
export class MicrosoftCallbackComponent implements OnInit {
  private static readonly CodeVerifierStorageKey = 'ms_code_verifier';
  private static readonly StateStorageKey = 'ms_oauth_state';
  private static readonly NonceStorageKey = 'ms_oauth_nonce';
  private static readonly FallbackMessage = MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE;
  private platformId = inject(PLATFORM_ID);

  status = signal<'loading' | 'success' | 'error' | 'device'>('loading');
  message = signal('Signing you in with Microsoft...');

  private authToken = inject(AuthTokenService);
  private providerLinkFlow = inject(ProviderLinkFlowService);
  private microsoftExchange = inject(MicrosoftTokenExchangeService);

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

    // Linking from security settings returns here too, from a signed-in session, so it is told
    // apart by the state it started before the signed-in redirect below can send it away.
    if (this.providerLinkFlow.isLinkCallback('microsoft', window.location.search)) {
      void this.completeProviderLink();
      return;
    }

    if (this.authToken.accessToken) {
      this.router.navigateByUrl(this.authReturnUrl.consume('/dashboard'));
      return;
    }

    void this.handleMicrosoftCallback();
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

      const idToken = await this.microsoftExchange.exchange(code, verifier);

      sessionStorage.removeItem(MicrosoftCallbackComponent.CodeVerifierStorageKey);
      sessionStorage.removeItem(MicrosoftCallbackComponent.StateStorageKey);
      sessionStorage.removeItem(MicrosoftCallbackComponent.NonceStorageKey);

      this.auth.microsoftVerify(idToken, nonce, this.authReturnUrl.peek() ?? undefined).subscribe({
        next: async (res: OAuthAuthenticationResponse) => {
          if (res.Type === 'requires_role_selection') {
            sessionStorage.setItem(PendingOAuthSignupStorageKey, JSON.stringify(res.RoleSelection));
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

          if (res.Type === 'requires_link_confirmation') {
            this.providerLinkFlow.proposeEmailLink(res.LinkConfirmation);
            this.status.set('success');
            this.message.set(
              'An account already uses this email. Sign in to it to link Microsoft...',
            );
            setTimeout(() => this.router.navigate(['/auth/login']), 250);
            return;
          }

          try {
            await this.sessionManager.bootstrapSession(res.Auth);
            this.status.set('success');
            this.message.set('Login successful! Redirecting...');
            const target = this.authReturnUrl.consume(res.Auth.ReturnPath ?? '/dashboard');
            setTimeout(() => this.router.navigateByUrl(target), 1500);
          } catch (err: any) {
            console.error(err);
            this.status.set('error');
            this.message.set(getApiClientMessage(err, 'Sign-in failed.'));
          }
        },
        error: (err) => {
          console.error(err);

          if (isApiClientErrorCode(err, DEVICE_VERIFICATION_REQUIRED_ERROR_CODE)) {
            this.status.set('device');
            this.message.set(DEVICE_VERIFICATION_REQUIRED_MESSAGE);
            return;
          }

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
    void this.handleMicrosoftCallback();
  }

  private async completeProviderLink(): Promise<void> {
    this.message.set('Linking your Microsoft account...');
    const target = await this.providerLinkFlow.completeFromCallback(
      'microsoft',
      window.location.search,
    );
    await this.router.navigateByUrl(target);
  }
}
