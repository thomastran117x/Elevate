import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';
import { environment } from '@environments/environment';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { AuthService } from '../../services/auth.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./forgot-password.component.css'],
})
export class ForgotPasswordComponent {
  private readonly fb = new FormBuilder();

  readonly siteKey = environment.googleSiteKey;
  readonly form = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
  });

  loading = false;
  error = '';
  success = '';

  constructor(
    private auth: AuthService,
    private recaptcha: RecaptchaV3Service,
  ) {}

  async submit(): Promise<void> {
    this.error = '';
    this.success = '';

    if (this.form.invalid) {
      return;
    }

    this.loading = true;

    try {
      const captcha = await this.recaptcha.execute(this.siteKey, 'forgot_password');
      this.auth
        .forgotPassword({
          email: this.form.getRawValue().email,
          captcha,
        })
        .pipe(finalize(() => (this.loading = false)))
        .subscribe({
          next: () => {
            this.success = 'If that account exists, a reset email has been sent.';
          },
          error: (err) => {
            this.error = getApiClientMessage(err, 'Unable to start password reset.');
          },
        });
    } catch (err: any) {
      this.loading = false;
      this.error = err?.message || 'Captcha failed to initialize.';
    }
  }
}
