import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, inject, PLATFORM_ID, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { setUser } from '../../../../core/stores/user.actions';
import { UserState } from '../../../../core/stores/user.reducer';
import {
  AuthService,
  PendingOAuthSignupStorageKey,
  SignupRole,
} from '../../services/auth.service';

type PendingOAuthSignup = {
  RequiresRoleSelection: true;
  SignupToken: string;
  Email: string;
  Name: string;
  Provider: string;
};

@Component({
  selector: 'app-oauth-role',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './oauth-role.component.html',
  styleUrls: ['./oauth-role.component.css'],
})
export class OAuthRoleComponent {
  private platformId = inject(PLATFORM_ID);
  private fb = inject(FormBuilder);

  readonly roleOptions: Array<{ value: SignupRole; label: string; description: string }> = [
    {
      value: 'participant',
      label: 'Participant',
      description: 'Join events, discover clubs, and follow what interests you.',
    },
    {
      value: 'organizer',
      label: 'Organizer',
      description: 'Create events, manage communities, and publish updates.',
    },
    {
      value: 'volunteer',
      label: 'Volunteer',
      description: 'Help run events and support organizers on the ground.',
    },
  ];

  readonly form = this.fb.nonNullable.group({
    usertype: this.fb.nonNullable.control<SignupRole>('participant', [Validators.required]),
  });

  readonly status = signal<'ready' | 'loading' | 'error'>('ready');
  readonly message = signal('Choose how you want to use EventXperience.');
  readonly pending = signal<PendingOAuthSignup | null>(null);

  constructor(
    private auth: AuthService,
    private store: Store<{ user: UserState }>,
    private router: Router,
  ) {}

  ngOnInit(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    const raw = sessionStorage.getItem(PendingOAuthSignupStorageKey);
    if (!raw) {
      this.status.set('error');
      this.message.set('Your OAuth signup session was not found. Please start again.');
      return;
    }

    try {
      const parsed = JSON.parse(raw) as PendingOAuthSignup;
      if (!parsed.SignupToken || !parsed.Email || !parsed.Provider) {
        throw new Error('Incomplete OAuth signup session.');
      }

      this.pending.set(parsed);
    } catch {
      sessionStorage.removeItem(PendingOAuthSignupStorageKey);
      this.status.set('error');
      this.message.set('Your OAuth signup session is invalid. Please start again.');
    }
  }

  submit(): void {
    if (this.status() === 'loading' || this.form.invalid) {
      return;
    }

    const pending = this.pending();
    if (!pending) {
      this.status.set('error');
      this.message.set('Your OAuth signup session is missing. Please start again.');
      return;
    }

    this.status.set('loading');
    this.message.set('Completing your account setup...');

    this.auth.completeOAuthSignup(pending.SignupToken, this.form.getRawValue().usertype).subscribe({
      next: (user) => {
        sessionStorage.removeItem(PendingOAuthSignupStorageKey);
        this.store.dispatch(setUser({ user }));
        this.status.set('ready');
        this.message.set('Your account is ready. Redirecting to your dashboard...');
        setTimeout(() => this.router.navigate(['/dashboard']), 800);
      },
      error: (err) => {
        this.status.set('error');
        this.message.set(err?.error?.message || 'We could not complete your signup. Please try again.');
      },
    });
  }
}
