import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthenticatedSessionResponse } from '../../../../core/models/auth-response.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthService, ApiEnvelope } from '../../services/auth.service';

@Component({
  selector: 'app-verify',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './verify.component.html',
  styleUrls: ['./verify.component.css'],
})
export class VerifyComponent {
  status = signal<'ready' | 'loading' | 'success' | 'error'>('ready');
  message = signal('Confirm your email to finish creating your account.');
  hasToken = false;

  private token: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private auth: AuthService,
    private sessionManager: SessionManagerService,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token');
    this.hasToken = !!this.token;

    if (!this.token) {
      this.status.set('error');
      this.message.set('This verification link is missing a token.');
    }
  }

  confirm(): void {
    if (!this.token || this.status() === 'loading') return;

    this.status.set('loading');
    this.message.set('Verifying your email and completing sign-in...');

    this.auth.verifyEmail(this.token).subscribe({
      next: async (res: ApiEnvelope<AuthenticatedSessionResponse>) => {
        const session = res.data ?? res.Data;
        if (!session) {
          this.status.set('error');
          this.message.set('Verification completed, but the login response was incomplete.');
          return;
        }

        try {
          await this.sessionManager.bootstrapSession(session);
          this.status.set('success');
          this.message.set('Your email is verified. Redirecting to your dashboard...');
          setTimeout(() => this.router.navigate(['/dashboard']), 1500);
        } catch (err: any) {
          this.status.set('error');
          this.message.set(err?.error?.message || err?.message || 'Email verification failed. Please try again.');
        }
      },
      error: (err) => {
        this.status.set('error');
        this.message.set(err?.error?.message || 'Email verification failed. Please try again.');
      },
    });
  }
}
