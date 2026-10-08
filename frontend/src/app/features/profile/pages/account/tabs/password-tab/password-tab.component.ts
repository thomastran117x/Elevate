import { CommonModule } from '@angular/common';
import { Component, ChangeDetectionStrategy } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../core/api/services/auth-token.service';
import { ProfileService } from '../../../../services/profile.service';
import { MfaGateComponent } from '../../mfa-gate/mfa-gate.component';

const MFA_REQUIRED_ERROR_CODE = 'MFA_REQUIRED';

const passwordsMatchValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const newPassword = group.get('newPassword')?.value;
  const confirmPassword = group.get('confirmPassword')?.value;
  return newPassword && confirmPassword && newPassword !== confirmPassword
    ? { passwordsMismatch: true }
    : null;
};

@Component({
  selector: 'app-password-tab',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MfaGateComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './password-tab.component.html',
})
export class PasswordTabComponent {
  private readonly fb = new FormBuilder();

  // The form is revealed only after the reusable gate confirms a fresh MFA
  // verification; the change-password endpoint is also [RequireMfa]-gated.
  mfaVerified = false;
  // Set when the server refused the change because the 10-minute step-up proof had
  // expired: the gate then prompts at once and the typed values are kept for a retry.
  stepUpLapsed = false;
  resumeNotice = '';

  readonly passwordForm = this.fb.nonNullable.group(
    {
      currentPassword: this.fb.nonNullable.control('', [Validators.required]),
      newPassword: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(8)]),
      confirmPassword: this.fb.nonNullable.control('', [Validators.required]),
    },
    { validators: passwordsMatchValidator },
  );

  saving = false;
  error = '';

  constructor(
    private profileService: ProfileService,
    private authToken: AuthTokenService,
    private router: Router,
  ) {}

  get passwordsMismatch(): boolean {
    return (
      this.passwordForm.hasError('passwordsMismatch') &&
      (this.passwordForm.controls.confirmPassword.touched ||
        this.passwordForm.controls.newPassword.touched)
    );
  }

  onMfaVerified(): void {
    this.mfaVerified = true;
    this.resumeNotice = this.stepUpLapsed ? 'Identity verified. Submit again to finish.' : '';
  }

  changePassword(): void {
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    const { currentPassword, newPassword } = this.passwordForm.getRawValue();
    this.saving = true;
    this.error = '';
    this.resumeNotice = '';

    this.profileService
      .changePassword(currentPassword, newPassword)
      .pipe(finalize(() => (this.saving = false)))
      .subscribe({
        next: () => {
          this.authToken.logoutLocal();
          this.router.navigate(['/auth/login']);
        },
        error: (err) => {
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.stepUpLapsed = true;
            this.mfaVerified = false;
            return;
          }
          this.error = getApiClientMessage(err, 'Unable to change password.');
        },
      });
  }
}
