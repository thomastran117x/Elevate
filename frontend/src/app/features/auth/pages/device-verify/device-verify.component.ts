import { isPlatformBrowser } from '@angular/common';
import { Component, inject, PLATFORM_ID, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthService } from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';

@Component({
  selector: 'app-device-verify',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './device-verify.component.html',
  styleUrls: ['./device-verify.component.css'],
})
export class DeviceVerifyComponent {
  private platformId = inject(PLATFORM_ID);

  status = signal<'ready' | 'loading' | 'success' | 'error'>('ready');
  message = signal(
    'Open the verification link on the device you want to use, and we will finish signing you in here.',
  );
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
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    this.authReturnUrl.captureFromRoute(this.route);
    this.token = this.route.snapshot.queryParamMap.get('token');
    this.hasToken = !!this.token;

    if (!this.token) {
      this.status.set('error');
      this.message.set('This device verification link is missing a token.');
      return;
    }

    this.confirm();
  }

  confirm(): void {
    if (!this.token || this.status() === 'loading') return;

    this.status.set('loading');
    this.message.set('Verifying this device and finishing sign-in...');

    this.auth.verifyDevice(this.token).subscribe({
      next: async (session) => {
        try {
          await this.sessionManager.bootstrapSession(session);
          this.status.set('success');
          this.message.set('This device is verified. Redirecting you back...');
          const target = this.authReturnUrl.consume(session.ReturnPath ?? '/dashboard');
          setTimeout(() => this.router.navigateByUrl(target), 1500);
        } catch (err: any) {
          this.status.set('error');
          this.message.set(
            getApiClientMessage(err, 'Device verification failed. Please try again.'),
          );
        }
      },
      error: (err) => {
        this.status.set('error');
        this.message.set(getApiClientMessage(err, 'Device verification failed. Please try again.'));
      },
    });
  }
}
