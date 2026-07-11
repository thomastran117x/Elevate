import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnInit, Output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { from } from 'rxjs';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../core/api/services/auth-token.service';
import {
  AuthService,
  SessionMfaMethod,
  SessionMfaOptionsResponse,
} from '../../../../auth/services/auth.service';

const MFA_REQUIRED_ERROR_CODE = 'MFA_REQUIRED';

/**
 * Reusable in-session MFA gate. On init it probes whether the current session
 * has completed a fresh MFA verification; if not, it shows a locked state with a
 * modal that walks the user through picking a method and entering a 6-digit code.
 * Emits {@link verified} once the session is verified (already or after the
 * modal), so the host can reveal the protected content.
 */
@Component({
  selector: 'app-mfa-gate',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './mfa-gate.component.html',
})
export class MfaGateComponent implements OnInit {
  @Output() verified = new EventEmitter<void>();

  private readonly fb = new FormBuilder();

  readonly codeForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^\d{6}$/)]),
  });

  checking = true;
  modalOpen = false;
  step: 'method' | 'code' = 'method';
  optionsLoading = false;
  options: SessionMfaOptionsResponse | null = null;
  method: SessionMfaMethod | null = null;
  maskedDestination = '';
  sending = false;
  verifying = false;
  error = '';
  private refreshAttempted = false;

  constructor(
    private auth: AuthService,
    private authToken: AuthTokenService,
  ) {}

  ngOnInit(): void {
    this.checkStatus();
  }

  get selectedNeedsDelivery(): boolean {
    return this.method === 'sms' || this.method === 'email';
  }

  methodLabel(method: SessionMfaMethod): string {
    return method === 'totp'
      ? 'Authenticator app'
      : method === 'sms'
        ? 'Text message (SMS)'
        : 'Email';
  }

  methodDescription(method: SessionMfaMethod): string {
    return method === 'totp'
      ? 'Enter a 6-digit code from your authenticator app.'
      : method === 'sms'
        ? 'We text a 6-digit code to your verified phone.'
        : 'We email a 6-digit code to your inbox.';
  }

  openModal(): void {
    this.modalOpen = true;
    this.step = 'method';
    this.method = null;
    this.maskedDestination = '';
    this.error = '';
    this.codeForm.reset();
    this.loadOptions();
  }

  closeModal(): void {
    if (this.sending || this.verifying) {
      return;
    }
    this.modalOpen = false;
    this.step = 'method';
    this.maskedDestination = '';
    this.error = '';
    this.codeForm.reset();
  }

  selectMethod(method: SessionMfaMethod): void {
    this.method = method;
    this.error = '';
  }

  continueToCode(): void {
    if (!this.method) {
      return;
    }

    // TOTP needs no delivery — go straight to code entry. SMS/email send first.
    if (!this.selectedNeedsDelivery) {
      this.error = '';
      this.codeForm.reset();
      this.step = 'code';
      return;
    }

    this.sendCode(true);
  }

  sendCode(advanceToCode = false): void {
    if (!this.method || !this.selectedNeedsDelivery) {
      return;
    }

    this.error = '';
    this.sending = true;

    this.auth
      .startSessionMfa(this.method)
      .pipe(finalize(() => (this.sending = false)))
      .subscribe({
        next: (res) => {
          this.maskedDestination = res.maskedDestination;
          if (advanceToCode) {
            this.codeForm.reset();
            this.step = 'code';
          }
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to send the verification code.');
        },
      });
  }

  backToMethodStep(): void {
    this.step = 'method';
    this.error = '';
    this.codeForm.reset();
  }

  verifyCode(): void {
    if (!this.method) {
      return;
    }

    if (this.codeForm.invalid) {
      this.codeForm.markAllAsTouched();
      return;
    }

    const { code } = this.codeForm.getRawValue();
    this.error = '';
    this.verifying = true;

    this.auth
      .verifySessionMfa(this.method, code)
      .pipe(finalize(() => (this.verifying = false)))
      .subscribe({
        next: () => {
          this.modalOpen = false;
          this.verified.emit();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to verify the code. Please try again.');
        },
      });
  }

  private checkStatus(): void {
    this.checking = true;

    this.auth
      .getSessionMfaStatus()
      .pipe(finalize(() => (this.checking = false)))
      .subscribe({
        next: () => this.verified.emit(),
        error: (err) => {
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.handleGated();
            return;
          }
          this.error = getApiClientMessage(err, 'Unable to check verification status.');
        },
      });
  }

  private handleGated(): void {
    // Sessions created before the sid-claim rollout carry no session id in their
    // access token, so a fresh token must be minted before verification can bind
    // to the session. Try one silent refresh, then re-check.
    if (!this.refreshAttempted) {
      this.refreshAttempted = true;
      this.checking = true;
      from(this.authToken.refreshAccessToken()).subscribe({
        next: () => this.checkStatus(),
        error: () => (this.checking = false),
      });
    }
  }

  private loadOptions(): void {
    this.optionsLoading = true;
    this.error = '';

    this.auth
      .getSessionMfaOptions()
      .pipe(finalize(() => (this.optionsLoading = false)))
      .subscribe({
        next: (options) => {
          this.options = options;
          this.method = options.availableMethods[0] ?? 'email';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load verification options.');
        },
      });
  }
}
