import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiEnvelope, requireEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import { AuthenticatedSessionResponse } from '../../../../core/models/auth-response.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthService } from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';

@Component({
  selector: 'app-device-verify',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './device-verify.component.html',
  styleUrls: ['./device-verify.component.css'],
})
export class DeviceVerifyComponent {
  status = signal<'ready' | 'loading' | 'success' | 'error'>('ready');
  message = signal('Confirm this device to finish signing in.');
  hasToken = false;

  private token: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private auth: AuthService,
    private sessionManager: SessionManagerService,
    private router: Router,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit(): void {
    this.authReturnUrl.captureFromRoute(this.route);
    this.token = this.route.snapshot.queryParamMap.get('token');
    this.hasToken = !!this.token;

    if (!this.token) {
      this.status.set('error');
      this.message.set('This device verification link is missing a token.');
    }
  }

  confirm(): void {
    if (!this.token || this.status() === 'loading') return;

    this.status.set('loading');
    this.message.set('Verifying this device and completing sign-in...');

    this.auth.verifyDevice(this.token).subscribe({
      next: async (res: ApiEnvelope<AuthenticatedSessionResponse>) => {
        try {
          const session = requireEnvelopeData(
            res,
            'Device verification completed, but the login response was incomplete.',
          );
          await this.sessionManager.bootstrapSession(session);
          this.status.set('success');
          this.message.set('This device is verified. Redirecting you back...');
          const target = this.authReturnUrl.consume();
          setTimeout(() => this.router.navigateByUrl(target), 1500);
        } catch (err: any) {
          this.status.set('error');
          this.message.set(err?.error?.message || err?.message || 'Device verification failed. Please try again.');
        }
      },
      error: (err) => {
        this.status.set('error');
        this.message.set(err?.error?.message || 'Device verification failed. Please try again.');
      },
    });
  }
}
