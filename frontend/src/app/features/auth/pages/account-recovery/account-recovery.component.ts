import { Component, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { environment } from '@environments/environment';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { requireEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import { AuthService } from '../../services/auth.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';

type RecoveryMode = 'password' | 'username';

@Component({
  selector: 'app-account-recovery',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './account-recovery.component.html',
  styleUrls: ['./account-recovery.component.css'],
})
export class AccountRecoveryComponent implements OnInit {
  private readonly fb = new FormBuilder();

  readonly siteKey = environment.googleSiteKey;
  readonly passwordForm = this.fb.nonNullable.group({
    username: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(50)]),
  });
  readonly usernameForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
  });

  mode: RecoveryMode = 'password';
  loading = false;
  error = '';
  success = '';

  constructor(
    private auth: AuthService,
    private recaptcha: RecaptchaV3Service,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.mode =
      this.route.snapshot.queryParamMap.get('mode') === 'username' ? 'username' : 'password';
  }

  setMode(mode: RecoveryMode): void {
    this.mode = mode;
    this.error = '';
    this.success = '';
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { mode },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  async submitPasswordRecovery(): Promise<void> {
    this.error = '';
    this.success = '';
    const username = this.passwordForm.getRawValue().username.trim().toLowerCase();
    this.passwordForm.controls.username.setValue(username);
    if (this.passwordForm.invalid) return;

    this.loading = true;
    try {
      const captcha = await this.recaptcha.execute(this.siteKey, 'recover_password');
      this.auth
        .recoverPassword({
          username,
          captcha,
        })
        .pipe(finalize(() => (this.loading = false)))
        .subscribe({
          next: (response) => {
            const challenge = requireEnvelopeData(
              response,
              'Password recovery response was incomplete.',
            );
            void this.router.navigate(['/auth/reset-password'], {
              queryParams: { challenge: challenge.Challenge },
            });
          },
          error: (err) => {
            this.error = getApiClientMessage(err, 'Unable to start password recovery.');
          },
        });
    } catch (err: any) {
      this.loading = false;
      this.error = err?.message || 'Captcha failed to initialize.';
    }
  }

  async submitUsernameRecovery(): Promise<void> {
    this.error = '';
    this.success = '';
    const email = this.usernameForm.getRawValue().email.trim();
    this.usernameForm.controls.email.setValue(email);
    if (this.usernameForm.invalid) return;

    this.loading = true;
    try {
      const captcha = await this.recaptcha.execute(this.siteKey, 'recover_username');
      this.auth
        .recoverUsername({
          email,
          captcha,
        })
        .pipe(finalize(() => (this.loading = false)))
        .subscribe({
          next: () => {
            this.success = 'If that account exists, recovery instructions have been sent.';
          },
          error: (err) => {
            this.error = getApiClientMessage(err, 'Unable to recover your username.');
          },
        });
    } catch (err: any) {
      this.loading = false;
      this.error = err?.message || 'Captcha failed to initialize.';
    }
  }
}
