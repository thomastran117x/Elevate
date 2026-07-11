import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { from } from 'rxjs';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../core/api/services/auth-token.service';
import {
  AuthService,
  MfaChallengeResponse,
  MfaSettingsResponse,
  SessionMfaMethod,
  SessionMfaOptionsResponse,
  TotpEnrollmentStartResponse,
} from '../../../../../auth/services/auth.service';

type SmsFlow = 'enroll' | 'enable' | null;
type TotpManageAction = 'enable' | 'disable' | 'remove' | null;

const MFA_REQUIRED_ERROR_CODE = 'MFA_REQUIRED';

@Component({
  selector: 'app-security-tab',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './security-tab.component.html',
})
export class SecurityTabComponent implements OnInit {
  private readonly fb = new FormBuilder();

  readonly phoneForm = this.fb.nonNullable.group({
    phoneNumber: this.fb.nonNullable.control('', [Validators.required]),
  });

  readonly smsCodeForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^\d{6}$/)]),
  });

  readonly totpSetupForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^\d{6}$/)]),
  });

  readonly totpManageForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^\d{6}$/)]),
  });

  readonly mfaGateForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^\d{6}$/)]),
  });

  settings: MfaSettingsResponse | null = null;
  smsChallenge: MfaChallengeResponse | null = null;
  smsFlow: SmsFlow = null;
  smsEditorOpen = false;
  totpEnrollment: TotpEnrollmentStartResponse | null = null;
  totpManageAction: TotpManageAction = null;
  loading = true;
  smsSubmitting = false;
  smsVerifying = false;
  smsMutating = false;
  totpStarting = false;
  totpVerifying = false;
  totpMutating = false;
  error = '';
  success = '';

  // In-session MFA gate: viewing this page requires a fresh MFA verification
  // (backend returns 403 MFA_REQUIRED until the session is verified).
  mfaGateRequired = false;
  mfaOptionsLoading = false;
  mfaOptions: SessionMfaOptionsResponse | null = null;
  mfaMethod: SessionMfaMethod | null = null;
  mfaCodeSent = false;
  mfaMaskedDestination = '';
  mfaSending = false;
  mfaVerifying = false;
  mfaError = '';
  private mfaRefreshAttempted = false;

  constructor(
    private auth: AuthService,
    private authToken: AuthTokenService,
  ) {}

  ngOnInit(): void {
    this.refreshStatus();
  }

  get mfaSelectedNeedsDelivery(): boolean {
    return this.mfaMethod === 'sms' || this.mfaMethod === 'email';
  }

  get mfaShowCodeInput(): boolean {
    return this.mfaMethod === 'totp' || this.mfaCodeSent;
  }

  mfaMethodLabel(method: SessionMfaMethod): string {
    return method === 'totp'
      ? 'Authenticator app'
      : method === 'sms'
        ? 'Text message (SMS)'
        : 'Email';
  }

  selectMfaMethod(method: SessionMfaMethod): void {
    if (this.mfaMethod === method) {
      return;
    }
    this.mfaMethod = method;
    this.mfaCodeSent = false;
    this.mfaMaskedDestination = '';
    this.mfaError = '';
    this.mfaGateForm.reset();
  }

  sendMfaCode(): void {
    if (!this.mfaMethod || !this.mfaSelectedNeedsDelivery) {
      return;
    }

    this.mfaError = '';
    this.mfaSending = true;

    this.auth
      .startSessionMfa(this.mfaMethod)
      .pipe(finalize(() => (this.mfaSending = false)))
      .subscribe({
        next: (res) => {
          this.mfaCodeSent = true;
          this.mfaMaskedDestination = res.maskedDestination;
          this.mfaGateForm.reset();
        },
        error: (err) => {
          this.mfaError = getApiClientMessage(err, 'Unable to send the verification code.');
        },
      });
  }

  verifyMfaCode(): void {
    if (!this.mfaMethod) {
      return;
    }

    if (this.mfaGateForm.invalid) {
      this.mfaGateForm.markAllAsTouched();
      return;
    }

    const { code } = this.mfaGateForm.getRawValue();
    this.mfaError = '';
    this.mfaVerifying = true;

    this.auth
      .verifySessionMfa(this.mfaMethod, code)
      .pipe(finalize(() => (this.mfaVerifying = false)))
      .subscribe({
        next: () => {
          this.resetMfaGate();
          this.refreshStatus();
        },
        error: (err) => {
          this.mfaError = getApiClientMessage(err, 'Unable to verify the code. Please try again.');
        },
      });
  }

  private resetMfaGate(): void {
    this.mfaGateRequired = false;
    this.mfaOptions = null;
    this.mfaMethod = null;
    this.mfaCodeSent = false;
    this.mfaMaskedDestination = '';
    this.mfaError = '';
    this.mfaGateForm.reset();
  }

  private handleMfaRequired(): void {
    // Sessions created before the sid-claim rollout have no session id in their
    // access token, so a fresh token must be minted before verification can bind
    // to the session. Try one silent refresh, then re-check; if still gated, prompt.
    if (!this.mfaRefreshAttempted) {
      this.mfaRefreshAttempted = true;
      this.loading = true;
      from(this.authToken.refreshAccessToken()).subscribe({
        next: () => this.refreshStatus(),
        error: () => {
          this.loading = false;
          this.enterMfaGate();
        },
      });
      return;
    }

    this.loading = false;
    this.enterMfaGate();
  }

  private enterMfaGate(): void {
    this.mfaGateRequired = true;
    this.mfaOptionsLoading = true;
    this.mfaError = '';

    this.auth
      .getSessionMfaOptions()
      .pipe(finalize(() => (this.mfaOptionsLoading = false)))
      .subscribe({
        next: (options) => {
          this.mfaOptions = options;
          this.mfaMethod = options.availableMethods[0] ?? 'email';
        },
        error: (err) => {
          this.mfaError = getApiClientMessage(err, 'Unable to load verification options.');
        },
      });
  }

  get emailSettings() {
    return this.settings?.email ?? null;
  }

  get smsSettings() {
    return this.settings?.sms ?? null;
  }

  get totpSettings() {
    return this.settings?.totp ?? null;
  }

  get smsVerifiedAtLabel(): string | null {
    const value = this.smsSettings?.phoneVerifiedAtUtc;
    return value ? new Date(value).toLocaleString() : null;
  }

  get totpEnrolledAtLabel(): string | null {
    const value = this.totpSettings?.enrolledAtUtc;
    return value ? new Date(value).toLocaleString() : null;
  }

  get totpDisabledAtLabel(): string | null {
    const value = this.totpSettings?.disabledAtUtc;
    return value ? new Date(value).toLocaleString() : null;
  }

  get isSmsVerificationStep(): boolean {
    return this.smsChallenge !== null;
  }

  get isTotpSetupStep(): boolean {
    return this.totpEnrollment !== null;
  }

  get isTotpManageStep(): boolean {
    return this.totpManageAction !== null;
  }

  openSmsEditor(): void {
    this.clearMessages();
    this.smsEditorOpen = true;
  }

  cancelSmsEditor(): void {
    this.smsEditorOpen = false;
    this.phoneForm.reset();
    this.clearMessages();
  }

  startSmsEnrollment(): void {
    this.clearMessages();

    if (this.phoneForm.invalid) {
      this.phoneForm.markAllAsTouched();
      return;
    }

    const { phoneNumber } = this.phoneForm.getRawValue();
    this.smsSubmitting = true;

    this.auth
      .startMfaEnrollment(phoneNumber)
      .pipe(finalize(() => (this.smsSubmitting = false)))
      .subscribe({
        next: (challenge) => {
          this.smsChallenge = challenge;
          this.smsFlow = 'enroll';
          this.smsCodeForm.reset();
          this.success = `Verification code sent to ${challenge.MaskedDestination}.`;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to start SMS MFA enrollment.');
        },
      });
  }

  startSmsEnable(): void {
    this.clearMessages();
    this.smsSubmitting = true;

    this.auth
      .startMfaEnable()
      .pipe(finalize(() => (this.smsSubmitting = false)))
      .subscribe({
        next: (challenge) => {
          this.smsChallenge = challenge;
          this.smsFlow = 'enable';
          this.smsCodeForm.reset();
          this.success = `Verification code sent to ${challenge.MaskedDestination}.`;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to send the SMS re-enable code.');
          this.refreshStatus(true);
        },
      });
  }

  verifySmsChallenge(): void {
    this.clearMessages();

    if (!this.smsChallenge) {
      this.error = 'Start SMS setup before verifying a code.';
      return;
    }

    if (this.smsCodeForm.invalid) {
      this.smsCodeForm.markAllAsTouched();
      return;
    }

    const { code } = this.smsCodeForm.getRawValue();
    this.smsVerifying = true;

    this.auth
      .verifyMfaEnrollment(code, this.smsChallenge.Challenge)
      .pipe(finalize(() => (this.smsVerifying = false)))
      .subscribe({
        next: (settings) => {
          this.settings = settings;
          const flow = this.smsFlow;
          this.resetSmsFlow();
          this.success =
            flow === 'enable' ? 'SMS MFA has been re-enabled.' : 'SMS MFA is now enabled.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to verify the SMS MFA code.');
          this.refreshStatus(true);
        },
      });
  }

  cancelSmsChallenge(): void {
    this.resetSmsFlow();
    this.clearMessages();
  }

  disableSms(): void {
    this.clearMessages();
    this.smsMutating = true;

    this.auth
      .disableMfa()
      .pipe(finalize(() => (this.smsMutating = false)))
      .subscribe({
        next: (settings) => {
          this.settings = settings;
          this.success = 'SMS MFA has been disabled.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to disable SMS MFA.');
          this.refreshStatus(true);
        },
      });
  }

  removeSms(): void {
    this.clearMessages();
    this.smsMutating = true;

    this.auth
      .removeMfa()
      .pipe(finalize(() => (this.smsMutating = false)))
      .subscribe({
        next: (settings) => {
          this.settings = settings;
          this.resetSmsFlow();
          this.success = 'SMS MFA has been removed.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to remove SMS MFA.');
          this.refreshStatus(true);
        },
      });
  }

  startTotpEnrollment(): void {
    this.clearMessages();
    this.totpStarting = true;

    this.auth
      .startTotpEnrollment()
      .pipe(finalize(() => (this.totpStarting = false)))
      .subscribe({
        next: (response) => {
          this.totpEnrollment = response;
          this.totpSetupForm.reset();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to start TOTP enrollment.');
          this.refreshStatus(true);
        },
      });
  }

  verifyTotpEnrollment(): void {
    this.clearMessages();

    if (!this.totpEnrollment) {
      this.error = 'Start TOTP setup before verifying a code.';
      return;
    }

    if (this.totpSetupForm.invalid) {
      this.totpSetupForm.markAllAsTouched();
      return;
    }

    const { code } = this.totpSetupForm.getRawValue();
    this.totpVerifying = true;

    this.auth
      .verifyTotpEnrollment(code)
      .pipe(finalize(() => (this.totpVerifying = false)))
      .subscribe({
        next: (settings) => {
          this.settings = settings;
          this.totpEnrollment = null;
          this.totpSetupForm.reset();
          this.success = 'TOTP MFA is now enabled.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to verify the TOTP code.');
          this.refreshStatus(true);
        },
      });
  }

  cancelTotpEnrollment(): void {
    this.totpEnrollment = null;
    this.totpSetupForm.reset();
    this.clearMessages();
  }

  beginTotpAction(action: Exclude<TotpManageAction, null>): void {
    this.clearMessages();
    this.totpManageAction = action;
    this.totpManageForm.reset();
  }

  submitTotpAction(): void {
    this.clearMessages();

    if (!this.totpManageAction) {
      return;
    }

    if (this.totpManageForm.invalid) {
      this.totpManageForm.markAllAsTouched();
      return;
    }

    const { code } = this.totpManageForm.getRawValue();
    const action = this.totpManageAction;
    this.totpMutating = true;

    const request =
      action === 'enable'
        ? this.auth.enableTotp(code)
        : action === 'disable'
          ? this.auth.disableTotp(code)
          : this.auth.removeTotp(code);

    request.pipe(finalize(() => (this.totpMutating = false))).subscribe({
      next: (settings) => {
        this.settings = settings;
        this.totpManageAction = null;
        this.totpManageForm.reset();
        this.success =
          action === 'enable'
            ? 'TOTP MFA has been re-enabled.'
            : action === 'disable'
              ? 'TOTP MFA has been disabled.'
              : 'TOTP MFA has been removed.';
      },
      error: (err) => {
        this.error = getApiClientMessage(err, 'Unable to update TOTP MFA.');
        this.refreshStatus(true);
      },
    });
  }

  cancelTotpAction(): void {
    this.totpManageAction = null;
    this.totpManageForm.reset();
    this.clearMessages();
  }

  private resetSmsFlow(): void {
    this.smsChallenge = null;
    this.smsFlow = null;
    this.smsEditorOpen = false;
    this.phoneForm.reset();
    this.smsCodeForm.reset();
  }

  private clearMessages(): void {
    this.error = '';
    this.success = '';
  }

  private refreshStatus(silent = false): void {
    this.loading = !silent;
    if (!silent) {
      this.error = '';
    }

    this.auth
      .getMfaStatus()
      .pipe(finalize(() => (this.loading = false)))
      .subscribe({
        next: (settings) => {
          this.settings = settings;
          this.mfaGateRequired = false;
        },
        error: (err) => {
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.handleMfaRequired();
            return;
          }

          if (!silent) {
            this.error = getApiClientMessage(err, 'Unable to load security settings.');
          }
        },
      });
  }
}
