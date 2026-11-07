import { CommonModule } from '@angular/common';
import { Component, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import {
  AuthService,
  LoginStepUpChallengeResponse,
  LoginStepUpMethod,
  PendingLoginStepUpStorageKey,
  StartLoginStepUpResponse,
} from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import {
  RECOVERY_CODE_HINT,
  RECOVERY_CODE_MAX_INPUT_LENGTH,
  verificationCodeValidators,
} from '../../validators/recovery-code.validator';

@Component({
  selector: 'app-step-up-verify',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './step-up-verify.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./step-up-verify.component.css'],
})
export class StepUpVerifyComponent {
  private readonly fb = new FormBuilder();

  readonly codeForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', verificationCodeValidators(null)),
  });

  readonly recoveryCodeHint = RECOVERY_CODE_HINT;
  readonly recoveryCodeMaxLength = RECOVERY_CODE_MAX_INPUT_LENGTH;

  readonly status = signal<'ready' | 'starting' | 'verifying' | 'success' | 'error'>('ready');
  readonly message = signal('Choose how you want to verify this sign-in.');
  readonly pending = signal<LoginStepUpChallengeResponse | null>(null);
  readonly activeMethod = signal<LoginStepUpMethod | null>(null);
  readonly delivery = signal<StartLoginStepUpResponse | null>(null);

  error = '';
  success = '';

  constructor(
    private auth: AuthService,
    private sessionManager: SessionManagerService,
    private router: Router,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit(): void {
    this.loadPendingState();
  }

  get availableMethods(): LoginStepUpMethod[] {
    return this.pending()?.AvailableMethods ?? [];
  }

  get maskedEmail(): string {
    return this.pending()?.MaskedEmail ?? '';
  }

  get maskedPhone(): string | null {
    return this.pending()?.MaskedPhone ?? null;
  }

  get isSmsActive(): boolean {
    return this.activeMethod() === 'sms';
  }

  get isEmailActive(): boolean {
    return this.activeMethod() === 'email';
  }

  get isTotpActive(): boolean {
    return this.activeMethod() === 'totp';
  }

  get isRecoveryCodeActive(): boolean {
    return this.activeMethod() === 'recovery_code';
  }

  /** Whether the active method is finished by typing a code here, rather than an email link. */
  get isCodeEntryActive(): boolean {
    return this.isSmsActive || this.isTotpActive || this.isRecoveryCodeActive;
  }

  selectMethod(method: LoginStepUpMethod): void {
    const pending = this.pending();
    if (!pending || !pending.AvailableMethods.includes(method) || this.status() === 'starting') {
      return;
    }

    this.error = '';
    this.success = '';
    this.status.set('starting');
    this.message.set(
      method === 'sms'
        ? 'Sending a security code...'
        : method === 'email'
          ? 'Sending a verification email...'
          : method === 'recovery_code'
            ? 'Preparing recovery code verification...'
            : 'Preparing authenticator verification...',
    );

    this.auth
      .startLoginStepUp(pending.Challenge, method)
      .pipe(finalize(() => this.status.set('ready')))
      .subscribe({
        next: (response) => {
          const nextPending: LoginStepUpChallengeResponse = {
            Challenge: response.Challenge,
            ExpiresAtUtc: response.ExpiresAtUtc,
            AvailableMethods: response.AvailableMethods,
            MaskedPhone: response.MaskedPhone ?? null,
            MaskedEmail: response.MaskedEmail,
          };

          this.pending.set(nextPending);
          this.delivery.set(response);
          this.activeMethod.set(response.SelectedMethod);
          this.persistPendingState(nextPending);
          this.applyCodeValidators(response.SelectedMethod);
          this.codeForm.reset();
          this.success =
            response.SelectedMethod === 'sms'
              ? `Security code sent to ${response.MaskedDestination}.`
              : response.SelectedMethod === 'email'
                ? `Verification email sent to ${response.MaskedDestination}.`
                : response.SelectedMethod === 'recovery_code'
                  ? ''
                  : 'Open your authenticator app and enter the current 6-digit code.';
          this.message.set(this.codePrompt(response.SelectedMethod));
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to start sign-in verification.');
          this.message.set('Choose how you want to verify this sign-in.');
        },
      });
  }

  verifyCode(): void {
    const pending = this.pending();
    if (
      !pending ||
      !this.isCodeEntryActive ||
      this.codeForm.invalid ||
      this.status() === 'verifying'
    ) {
      this.codeForm.markAllAsTouched();
      return;
    }

    this.error = '';
    this.success = '';
    this.status.set('verifying');
    this.message.set('Verifying your code and finishing sign-in...');

    const { code } = this.codeForm.getRawValue();
    const verificationRequest = this.isTotpActive
      ? this.auth.verifyTotpLoginStepUp(pending.Challenge, code)
      : this.isRecoveryCodeActive
        ? this.auth.verifyRecoveryCodeLoginStepUp(pending.Challenge, code)
        : this.auth.verifyLoginStepUp(pending.Challenge, code);

    verificationRequest.pipe(finalize(() => this.status.set('ready'))).subscribe({
      next: async (session) => {
        try {
          await this.sessionManager.bootstrapSession(session);
          this.clearPendingState();
          this.status.set('success');
          this.message.set('Sign-in verified. Redirecting you back...');
          const target = this.authReturnUrl.consume(session.ReturnPath ?? '/dashboard');
          setTimeout(() => this.router.navigateByUrl(target), 800);
        } catch (err: any) {
          this.status.set('error');
          this.message.set(getApiClientMessage(err, 'Unable to complete sign-in.'));
        }
      },
      error: (err) => {
        this.error = getApiClientMessage(err, 'That code could not be verified.');
        this.message.set(this.codePrompt(this.activeMethod()));
      },
    });
  }

  restart(): void {
    this.error = '';
    this.success = '';
    this.activeMethod.set(null);
    this.delivery.set(null);
    this.codeForm.reset();
    this.message.set('Choose how you want to verify this sign-in.');
  }

  private codePrompt(method: LoginStepUpMethod | null): string {
    switch (method) {
      case 'sms':
        return 'Enter the code from your text message.';
      case 'email':
        return 'Check your email and open the verification link to finish signing in.';
      case 'recovery_code':
        return 'Enter one of your saved recovery codes to finish signing in.';
      default:
        return 'Enter the 6-digit code from your authenticator app to finish signing in.';
    }
  }

  /** A recovery code is not six digits, so the field's rules follow the chosen method. */
  private applyCodeValidators(method: LoginStepUpMethod): void {
    const control = this.codeForm.controls.code;
    control.setValidators(verificationCodeValidators(method));
    control.updateValueAndValidity();
  }

  private loadPendingState(): void {
    if (typeof sessionStorage === 'undefined') {
      this.status.set('error');
      this.message.set('This sign-in verification session is unavailable. Please sign in again.');
      return;
    }

    const raw = sessionStorage.getItem(PendingLoginStepUpStorageKey);
    if (!raw) {
      this.status.set('error');
      this.message.set('This sign-in verification session is missing. Please sign in again.');
      return;
    }

    try {
      const parsed = JSON.parse(raw) as LoginStepUpChallengeResponse;
      if (!parsed.Challenge || !parsed.ExpiresAtUtc || !Array.isArray(parsed.AvailableMethods)) {
        throw new Error('Incomplete sign-in verification state.');
      }

      this.pending.set(parsed);
    } catch {
      this.clearPendingState();
      this.status.set('error');
      this.message.set('This sign-in verification session is invalid. Please sign in again.');
    }
  }

  private persistPendingState(state: LoginStepUpChallengeResponse): void {
    if (typeof sessionStorage === 'undefined') {
      return;
    }

    sessionStorage.setItem(PendingLoginStepUpStorageKey, JSON.stringify(state));
  }

  private clearPendingState(): void {
    if (typeof sessionStorage !== 'undefined') {
      sessionStorage.removeItem(PendingLoginStepUpStorageKey);
    }
  }
}
