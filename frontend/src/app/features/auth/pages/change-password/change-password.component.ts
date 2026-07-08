import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './change-password.component.html',
  styleUrls: ['./change-password.component.css'],
})
export class ChangePasswordComponent {
  private readonly fb = new FormBuilder();

  readonly form = this.fb.nonNullable.group({
    password: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(8)]),
    confirmPassword: this.fb.nonNullable.control('', [Validators.required]),
  });

  loading = false;
  error = '';
  success = '';
  token: string | null = null;

  constructor(
    private auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token');
    if (!this.token) {
      this.error = 'This password reset link is missing a token.';
    }
  }

  submit(): void {
    this.error = '';
    this.success = '';

    if (!this.token) {
      this.error = 'This password reset link is missing a token.';
      return;
    }

    if (this.form.invalid) {
      return;
    }

    const { password, confirmPassword } = this.form.getRawValue();
    if (password !== confirmPassword) {
      this.error = 'Passwords do not match.';
      return;
    }

    this.loading = true;
    this.auth
      .changePassword(password, this.token)
      .pipe(finalize(() => (this.loading = false)))
      .subscribe({
        next: () => {
          this.success = 'Password updated successfully. Redirecting to sign in...';
          setTimeout(() => this.router.navigate(['/auth/login']), 1000);
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to update password.');
        },
      });
  }
}
