import { Component } from '@angular/core';
import { FormBuilder, Validators, FormGroup } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Store } from '@ngrx/store';
import { AuthService } from '../../services/auth.service';
import { setUser } from '@stores/user.actions';
import { UserState } from '@stores/user.reducer';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs/operators';
import { RecaptchaV3Service } from '../../services/recaptcha.service';
import { GoogleButtonComponent } from '../../components/google-button/google-button.component';
import { MicrosoftButtonComponent } from '../../components/microsoft-button/microsoft-button.component';
import { environment } from '@environments/environment';

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
  showPw = false;
  submitted = false;
  siteKey = environment.googleSiteKey;

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private store: Store<{ user: UserState }>,
    private router: Router,
    private recaptcha: RecaptchaV3Service,
  ) {}

  ngOnInit() {
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
    try {
      const token = await this.recaptcha.execute(this.siteKey, 'login');
      const payload = { ...this.form.value, captcha: token };

      this.auth
        .login(payload)
        .pipe(finalize(() => (this.loading = false)))
        .subscribe({
          next: (res) => {
            this.store.dispatch(setUser({ user: res }));
            this.router.navigate(['/dashboard']);
          },
          error: (err) => (this.error = err?.error?.message || 'Login failed.'),
        });
    } catch (e: any) {
      this.loading = false;
      this.error = e?.message || 'Captcha failed to initialize.';
    }
  }
}
