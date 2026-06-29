import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs/operators';

import { environment } from '@environments/environment';
import {
  DEVICE_VERIFICATION_REQUIRED_ERROR_CODE,
  DEVICE_VERIFICATION_REQUIRED_MESSAGE,
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { GoogleButtonComponent } from '../../components/google-button/google-button.component';
import { MicrosoftButtonComponent } from '../../components/microsoft-button/microsoft-button.component';
import { AuthService, PendingLoginStepUpStorageKey } from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    GoogleButtonComponent,
    MicrosoftButtonComponent,
  ],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
})
export class LoginComponent {
  form!: FormGroup;
  loading = false;
  error = '';
  notice = '';
  showPw = false;
  submitted = false;
  siteKey = environment.googleSiteKey;

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private sessionManager: SessionManagerService,
    private router: Router,
    private route: ActivatedRoute,
    private recaptcha: RecaptchaV3Service,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit() {
    this.authReturnUrl.captureFromRoute(this.route);
    this.form = this.fb.nonNullable.group({
      email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
      password: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(6)]),
      rememberMe: this.fb.nonNullable.control(false),
    });
  }

  togglePassword() {
    this.showPw = !this.showPw;
  }

  async onSubmit() {
    this.submitted = true;
    if (this.form.invalid) return;

    this.loading = true;
    this.error = '';
    this.notice = '';

    try {
      const token = await this.recaptcha.execute(this.siteKey, 'login');
      const payload = {
        ...this.form.getRawValue(),
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
}
