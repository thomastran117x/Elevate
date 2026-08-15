import { Component, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./reset-password.component.css'],
})
export class ResetPasswordComponent implements OnInit {
  private readonly fb = new FormBuilder();

  readonly form = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.pattern(/^\d{6}$/)]),
    password: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(8)]),
    confirmPassword: this.fb.nonNullable.control('', [Validators.required]),
  });

  loading = false;
  error = '';
  success = '';
  token: string | null = null;
  challenge: string | null = null;

  constructor(
    private auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token');
    this.challenge = this.route.snapshot.queryParamMap.get('challenge');
    if (!this.token && !this.challenge) {
      this.error = 'This password reset request is missing its recovery proof.';
    }
    if (this.challenge) {
      this.form.controls.code.addValidators(Validators.required);
      this.form.controls.code.updateValueAndValidity();
    }
  }

  submit(): void {
    this.error = '';
    this.success = '';
    if (!this.token && !this.challenge) {
      this.error = 'This password reset request is missing its recovery proof.';
      return;
    }
    if (this.form.invalid) return;

    const { code, password, confirmPassword } = this.form.getRawValue();
    if (password !== confirmPassword) {
      this.error = 'Passwords do not match.';
      return;
    }

    this.loading = true;
    this.auth
      .resetPassword(
        {
          password,
          ...(this.challenge ? { code, challenge: this.challenge } : {}),
        },
        this.token ?? undefined,
      )
      .pipe(finalize(() => (this.loading = false)))
      .subscribe({
        next: () => {
          this.success = 'Password updated successfully. Redirecting to sign in...';
          setTimeout(() => void this.router.navigate(['/auth/login']), 1000);
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to update password.');
        },
      });
  }
}
