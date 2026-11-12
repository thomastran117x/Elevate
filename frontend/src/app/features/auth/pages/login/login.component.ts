import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  afterNextRender,
  inject,
} from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs/operators';

import { environment } from '@environments/environment';
import { PillComponent } from '@common/pill/pill.component';
import {
  DEVICE_VERIFICATION_REQUIRED_ERROR_CODE,
  DEVICE_VERIFICATION_REQUIRED_MESSAGE,
  getApiClientMessage,
  isApiClientClientError,
  isApiClientErrorCode,
} from '../../../../core/api/models/api-client-error.model';
import { FeatureFlagsService } from '../../../../core/features/feature-flags.service';
import { FEATURE_KEYS } from '../../../../core/features/feature-flags.types';
import { AuthenticatedSessionResponse } from '../../../../core/models/auth-response.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { GoogleButtonComponent } from '../../components/google-button/google-button.component';
import { MicrosoftButtonComponent } from '../../components/microsoft-button/microsoft-button.component';
import { AuthService, PendingLoginStepUpStorageKey } from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import { PasskeyOutcome, PasskeysService } from '../../services/passkeys.service';
import { ProviderLinkFlowService } from '../../services/provider-link-flow.service';
import { PendingEmailLinkProposal, PROVIDER_LABELS } from '../../services/provider-links.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterModule,
    GoogleButtonComponent,
    MicrosoftButtonComponent,
    PillComponent,
  ],
  templateUrl: './login.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./login.component.css'],
})
export class LoginComponent {
  form!: FormGroup;
  loading = false;
  error = '';
  notice = '';
  showPw = false;
  capsLockOn = false;
  submitted = false;
  captchaRequired = false;
  siteKey = environment.googleSiteKey;
  /** A provider sign-in matched an existing account by email; signing in to it confirms the link. */
  linkProposal: PendingEmailLinkProposal | null = null;
  readonly providerLabels = PROVIDER_LABELS;
  /** Set after the first browser render, so server and client render the same markup. */
  passkeyAvailable = false;
  passkeyLoading = false;

  private readonly passkeys = inject(PasskeysService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  /** The autofill (conditional) passkey request waiting on the username field, if any. */
  private conditionalRequest: AbortController | null = null;

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private sessionManager: SessionManagerService,
    private router: Router,
    private route: ActivatedRoute,
    private recaptcha: RecaptchaV3Service,
    private authReturnUrl: AuthReturnUrlService,
    private providerLinkFlow: ProviderLinkFlowService,
  ) {
    const passkeysEnabled = inject(FeatureFlagsService).isEnabled(FEATURE_KEYS.authPasskeys);
    afterNextRender(() => {
      if (!passkeysEnabled || !this.passkeys.isSupported()) {
        return;
      }
      this.passkeyAvailable = true;
      this.changeDetector.markForCheck();
      void this.startConditionalPasskeySignIn();
    });
    inject(DestroyRef).onDestroy(() => this.abortConditionalPasskeySignIn());
  }

  ngOnInit() {
    this.authReturnUrl.captureFromRoute(this.route);
    this.linkProposal = this.providerLinkFlow.readEmailProposal();
    this.form = this.fb.nonNullable.group({
      username: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(50)]),
      password: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(6)]),
      rememberMe: this.fb.nonNullable.control(false),
    });
  }

  /** Forgets the proposed link; signing in then simply signs in. */
  dismissLinkProposal(): void {
    this.providerLinkFlow.declineEmailProposal();
    this.linkProposal = null;
  }

  togglePassword() {
    this.showPw = !this.showPw;
  }

  updateCapsLockState(event: KeyboardEvent): void {
    this.capsLockOn = event.getModifierState('CapsLock');
  }

  clearCapsLockState(): void {
    this.capsLockOn = false;
  }

  /** Asks the browser for any passkey this device holds for the site. */
  signInWithPasskey(): void {
    if (this.passkeyLoading) {
      return;
    }

    // Only one WebAuthn request can be pending, so the autofill one gives way.
    this.abortConditionalPasskeySignIn();
    this.passkeyLoading = true;
    this.error = '';
    this.notice = '';

    this.passkeys
      .signIn(this.form.controls['rememberMe'].value === true)
      .pipe(
        finalize(() => {
          this.passkeyLoading = false;
          this.changeDetector.markForCheck();
        }),
      )
      .subscribe({
        next: (outcome) => void this.handlePasskeyOutcome(outcome, true),
        error: (err) => {
          this.error = getApiClientMessage(err, 'Passkey sign-in failed. Please try again.');
          this.changeDetector.markForCheck();
        },
      });
  }

  async onSubmit() {
    this.submitted = true;
    if (this.form.invalid) return;

    this.abortConditionalPasskeySignIn();

    this.loading = true;
    this.error = '';
    this.notice = '';

    try {
      const token = this.captchaRequired
        ? await this.recaptcha.execute(this.siteKey, 'login')
        : undefined;
      const values = this.form.getRawValue();
      const username = values.username.trim().toLowerCase();
      this.form.controls['username'].setValue(username);
      const payload = {
        ...values,
        username,
        captcha: token,
        returnUrl: this.authReturnUrl.peek() ?? undefined,
      };

      this.auth
        .login(payload)
        .pipe(finalize(() => (this.loading = false)))
        .subscribe({
          next: async (res) => {
            try {
              if (res.Type === 'requires_step_up') {
                sessionStorage.setItem(PendingLoginStepUpStorageKey, JSON.stringify(res.StepUp));
                await this.router.navigate(['/auth/mfa']);
                return;
              }

              await this.sessionManager.bootstrapSession(res.Auth);
              await this.router.navigateByUrl(
                this.authReturnUrl.consume(res.Auth.ReturnPath ?? '/dashboard'),
              );
            } catch (err: any) {
              this.error = getApiClientMessage(err, 'Login failed.');
            }
          },
          error: (err) => {
            if (isApiClientClientError(err) && this.requiresCaptcha(err.meta)) {
              this.captchaRequired = true;
            }

            if (isApiClientErrorCode(err, DEVICE_VERIFICATION_REQUIRED_ERROR_CODE)) {
              this.notice = DEVICE_VERIFICATION_REQUIRED_MESSAGE;
              this.error = '';
              return;
            }

            this.notice = '';
            this.error = getApiClientMessage(err, 'Login failed.');
          },
        });
    } catch (e: any) {
      this.loading = false;
      this.error = e?.message || 'Captcha failed to initialize.';
      this.notice = '';
    }
  }

  /**
   * Offers passkeys in the username field's autofill. The request waits until the user picks
   * one there, and is aborted as soon as they sign in another way.
   */
  private async startConditionalPasskeySignIn(): Promise<void> {
    if (!(await this.passkeys.isConditionalMediationAvailable())) {
      return;
    }

    const request = new AbortController();
    this.conditionalRequest = request;
    this.passkeys.signIn(false, { mediation: 'conditional', signal: request.signal }).subscribe({
      next: (outcome) => {
        if (this.conditionalRequest === request) {
          this.conditionalRequest = null;
        }
        // A request this page aborted has nothing to report.
        if (!request.signal.aborted) {
          void this.handlePasskeyOutcome(outcome, false);
        }
      },
      error: (err) => {
        if (request.signal.aborted) {
          return;
        }
        this.error = getApiClientMessage(err, 'Passkey sign-in failed. Please try again.');
        this.changeDetector.markForCheck();
      },
    });
  }

  private abortConditionalPasskeySignIn(): void {
    this.conditionalRequest?.abort();
    this.conditionalRequest = null;
  }

  /** @param explicit The user pressed the passkey button, so a dismissed prompt is not silent. */
  private async handlePasskeyOutcome(
    outcome: PasskeyOutcome<AuthenticatedSessionResponse>,
    explicit: boolean,
  ): Promise<void> {
    if (outcome.status === 'done') {
      try {
        await this.sessionManager.bootstrapSession(outcome.value);
        await this.router.navigateByUrl(this.authReturnUrl.consume('/dashboard'));
      } catch (err: unknown) {
        this.error = getApiClientMessage(err, 'Passkey sign-in failed. Please try again.');
      }
    } else if (outcome.status === 'unsupported') {
      this.error = "This browser can't use passkeys. Sign in with your password instead.";
    } else if (outcome.status === 'failed') {
      this.error = 'Passkey sign-in failed. Try again or sign in with your password.';
    } else if (explicit && outcome.status === 'cancelled') {
      this.notice = 'Passkey sign-in was cancelled. Try again or sign in with your password.';
    }
    this.changeDetector.markForCheck();
  }

  private requiresCaptcha(meta: unknown): boolean {
    return (
      typeof meta === 'object' &&
      meta !== null &&
      (meta as Record<string, unknown>)['captchaRequired'] === true
    );
  }
}
