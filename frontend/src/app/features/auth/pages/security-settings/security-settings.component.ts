import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import {
  AuthService,
  MfaChallengeResponse,
  MfaSettingsResponse,
  TotpEnrollmentStartResponse,
} from '../../services/auth.service';

type SmsFlow = 'enroll' | 'enable' | null;
type TotpManageAction = 'enable' | 'disable' | 'remove' | null;

@Component({
  selector: 'app-security-settings',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './security-settings.component.html',
  styleUrls: ['./security-settings.component.css'],
})
export class SecuritySettingsComponent implements OnInit {
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

  constructor(private auth: AuthService) {}

  ngOnInit(): void {
    this.refreshStatus();
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
    this.totpMutating = true;

    const request =
      this.totpManageAction === 'enable'
        ? this.auth.enableTotp(code)
        : this.totpManageAction === 'disable'
          ? this.auth.disableTotp(code)
          : this.auth.removeTotp(code);

    request.pipe(finalize(() => (this.totpMutating = false))).subscribe({
      next: (settings) => {
        const action = this.totpManageAction;
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
        },
        error: (err) => {
          if (!silent) {
            this.error = getApiClientMessage(err, 'Unable to load security settings.');
          }
        },
      });
  }
}
