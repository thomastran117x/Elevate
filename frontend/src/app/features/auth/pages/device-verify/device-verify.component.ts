import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { setUser } from '../../../../core/stores/user.actions';
import { UserState } from '../../../../core/stores/user.reducer';
import { ApiEnvelope, AuthResponse, AuthService } from '../../services/auth.service';

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
    private store: Store<{ user: UserState }>,
    private router: Router,
  ) {}

  ngOnInit(): void {
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
      next: (res: ApiEnvelope<AuthResponse>) => {
        const user = res.data ?? res.Data;
        if (!user) {
          this.status.set('error');
          this.message.set('Device verification completed, but the login response was incomplete.');
          return;
        }

        this.store.dispatch(setUser({ user }));
        this.status.set('success');
        this.message.set('This device is verified. Redirecting to your dashboard...');
        setTimeout(() => this.router.navigate(['/dashboard']), 1500);
      },
      error: (err) => {
        this.status.set('error');
        this.message.set(err?.error?.message || 'Device verification failed. Please try again.');
      },
    });
  }
}
