import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';
import { environment } from '@environments/environment';
import { AuthService, SignupRole } from '../../services/auth.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';

@Component({
  selector: 'app-signup',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './signup.component.html',
  styleUrls: ['./signup.component.css'],
})
export class SignupComponent {
  private readonly fb = new FormBuilder();

  readonly siteKey = environment.googleSiteKey;
  readonly roleOptions: Array<{ value: SignupRole; label: string }> = [
    { value: 'participant', label: 'Participant' },
    { value: 'organizer', label: 'Organizer' },
    { value: 'volunteer', label: 'Volunteer' },
  ];

  readonly form = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    password: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(8)]),
    usertype: this.fb.nonNullable.control<SignupRole>('participant', [Validators.required]),
  });

  loading = false;
  submitted = false;
  error = '';
  success = '';

  constructor(
    private auth: AuthService,
    private recaptcha: RecaptchaV3Service,
    private route: ActivatedRoute,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit(): void {
    this.authReturnUrl.captureFromRoute(this.route);
  }

  async submit(): Promise<void> {
    this.submitted = true;
    this.error = '';
    this.success = '';

    if (this.form.invalid) {
      return;
    }

    this.loading = true;

    try {
      const captcha = await this.recaptcha.execute(this.siteKey, 'signup');
      this.auth
        .signup({
          ...this.form.getRawValue(),
          captcha,
        })
        .pipe(finalize(() => (this.loading = false)))
        .subscribe({
          next: () => {
            this.success =
              'Verification email sent. Check your inbox to finish creating your account.';
          },
          error: (err) => {
            this.error = err?.error?.message || 'Signup failed.';
          },
        });
    } catch (err: any) {
      this.loading = false;
      this.error = err?.message || 'Captcha failed to initialize.';
    }
  }
}
