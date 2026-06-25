import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { AuthService, MfaChallengeResponse, MfaStatusResponse } from '../../services/auth.service';

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

  readonly codeForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^\d{6}$/)]),
  });

  status: MfaStatusResponse | null = null;
  challenge: MfaChallengeResponse | null = null;
  loading = true;
  submittingPhone = false;
  verifying = false;
  disabling = false;
  error = '';
  success = '';
  private lastSubmittedPhoneNumber = '';

  constructor(private auth: AuthService) {}

  ngOnInit(): void {
    this.refreshStatus();
  }

  get isVerificationStep(): boolean {
    return this.challenge !== null;
  }

  get isEnabled(): boolean {
    return this.status?.IsSmsMfaEnabled ?? false;
  }

  get maskedPhoneNumber(): string | null {
    return this.status?.MaskedPhoneNumber ?? null;
  }

  get verifiedAtLabel(): string | null {
    if (!this.status?.PhoneVerifiedAtUtc) {
      return null;
    }

    return new Date(this.status.PhoneVerifiedAtUtc).toLocaleString();
  }

  startEnrollment(): void {
    this.error = '';
    this.success = '';

    if (this.phoneForm.invalid) {
      this.phoneForm.markAllAsTouched();
      return;
    }

    const { phoneNumber } = this.phoneForm.getRawValue();
    this.submittingPhone = true;
    this.lastSubmittedPhoneNumber = phoneNumber;

    this.auth
      .startMfaEnrollment(phoneNumber)
      .pipe(finalize(() => (this.submittingPhone = false)))
      .subscribe({
        next: (challenge) => {
          this.challenge = challenge;
          this.codeForm.reset();
          this.success = `Verification code sent to ${challenge.MaskedDestination}.`;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to start SMS MFA enrollment.');
        },
      });
  }

  verifyEnrollment(): void {
    this.error = '';
    this.success = '';

    if (!this.challenge) {
      this.error = 'Start enrollment before verifying a code.';
      return;
    }

    if (this.codeForm.invalid) {
      this.codeForm.markAllAsTouched();
      return;
    }

    const { code } = this.codeForm.getRawValue();
    this.verifying = true;

    this.auth
      .verifyMfaEnrollment(code, this.challenge.Challenge)
      .pipe(finalize(() => (this.verifying = false)))
      .subscribe({
        next: (status) => {
          this.status = status;
          this.challenge = null;
          this.codeForm.reset();
          this.phoneForm.reset();
          this.success = 'SMS MFA is now enabled for your account.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to verify the SMS MFA code.');
        },
      });
  }

  returnToPhoneStep(): void {
    this.challenge = null;
    this.codeForm.reset();
    this.error = '';
    this.success = '';

    if (this.lastSubmittedPhoneNumber) {
      this.phoneForm.patchValue({ phoneNumber: this.lastSubmittedPhoneNumber });
    }
  }

  disableMfa(): void {
    this.error = '';
    this.success = '';
    this.disabling = true;

    this.auth
      .disableMfa()
      .pipe(finalize(() => (this.disabling = false)))
      .subscribe({
        next: (status) => {
          this.status = status;
          this.challenge = null;
          this.codeForm.reset();
          this.success = 'SMS MFA has been disabled.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to disable SMS MFA.');
        },
      });
  }

  private refreshStatus(): void {
    this.loading = true;
    this.error = '';

    this.auth
      .getMfaStatus()
      .pipe(finalize(() => (this.loading = false)))
      .subscribe({
        next: (status) => {
          this.status = status;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load security settings.');
        },
      });
  }
}
