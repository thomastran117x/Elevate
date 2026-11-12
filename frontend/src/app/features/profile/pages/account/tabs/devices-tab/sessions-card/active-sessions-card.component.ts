import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../../core/api/services/auth-token.service';
import { ConfirmDialogComponent } from '../../../../../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  AccountDevicesService,
  AccountSession,
  SessionRevocation,
} from '../../../../../services/account-devices.service';
import { MfaGateComponent } from '../../../mfa-gate/mfa-gate.component';
import { deviceDescription } from '../device-labels';
import { PendingStepUp } from '../pending-step-up';

type SessionAction =
  { kind: 'one'; session: AccountSession } | { kind: 'others' } | { kind: 'all' };

interface Banner {
  tone: 'success' | 'error';
  text: string;
}

interface ConfirmCopy {
  title: string;
  body: string;
  impacts: string[];
  confirmLabel: string;
}

/**
 * The account's signed-in sessions, and signing out one, every other, or every session. Listing
 * needs no step-up; a sign-out refused for a missing proof opens verification and then finishes.
 * Ending this browser's own session drops the local session and returns to sign-in.
 */
@Component({
  selector: 'app-active-sessions-card',
  standalone: true,
  imports: [DatePipe, ConfirmDialogComponent, MfaGateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './active-sessions-card.component.html',
})
export class ActiveSessionsCardComponent implements OnInit {
  readonly sessions = signal<AccountSession[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly confirming = signal<SessionAction | null>(null);
  readonly busy = signal(false);
  readonly banner = signal<Banner | null>(null);
  readonly stepUp = new PendingStepUp();

  readonly otherCount = computed(() => this.sessions().filter((s) => !s.current).length);
  readonly confirmCopy = computed(() => {
    const action = this.confirming();
    return action ? confirmCopyFor(action) : null;
  });

  private readonly bannerRef = viewChild<ElementRef<HTMLElement>>('bannerRef');
  private readonly devices = inject(AccountDevicesService);
  private readonly authToken = inject(AuthTokenService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  ngOnInit(): void {
    this.load();
  }

  describe(session: AccountSession): string {
    return deviceDescription(session.client, session.deviceClass);
  }

  request(action: SessionAction): void {
    this.banner.set(null);
    this.confirming.set(action);
  }

  onConfirmResolved(confirmed: boolean): void {
    const action = this.confirming();
    if (!confirmed || !action) {
      this.confirming.set(null);
      return;
    }
    this.run(action);
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.devices
      .listSessions()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (sessions) => this.sessions.set(sessions),
        error: (err) =>
          this.loadError.set(getApiClientMessage(err, 'Unable to load your sessions.')),
      });
  }

  private run(action: SessionAction): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.revoke(action)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.confirming.set(null);
          if (result.currentSessionRevoked) {
            // This browser's tokens no longer work and the server has cleared its cookies.
            this.authToken.logoutLocal();
            void this.router.navigate(['/auth/login']);
            return;
          }
          this.show('success', successMessage(action, result));
          this.load();
        },
        error: (err) => {
          this.confirming.set(null);
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.stepUp.request(() => this.run(action));
            return;
          }
          this.show('error', getApiClientMessage(err, 'Unable to sign out. Please try again.'));
          this.load();
        },
      });
  }

  private revoke(action: SessionAction): Observable<SessionRevocation> {
    switch (action.kind) {
      case 'one':
        return this.devices.revokeSession(action.session.id);
      case 'others':
        return this.devices.revokeOtherSessions();
      default:
        return this.devices.revokeAllSessions();
    }
  }

  /** Screen reader and keyboard users land on the result rather than on a button that moved. */
  private show(tone: Banner['tone'], text: string): void {
    this.banner.set({ tone, text });
    afterNextRender(() => this.bannerRef()?.nativeElement.focus(), { injector: this.injector });
  }
}

function confirmCopyFor(action: SessionAction): ConfirmCopy {
  switch (action.kind) {
    case 'one':
      return action.session.current
        ? {
            title: 'Sign out of this device?',
            body: 'This is the session you are using now.',
            impacts: ['You will be signed out here and returned to the sign-in page.'],
            confirmLabel: 'Sign out',
          }
        : {
            title: `Sign out ${deviceDescription(action.session.client, action.session.deviceClass)}?`,
            body: 'That session stops working straight away.',
            impacts: [
              'Whoever is using it is signed out and has to sign in again.',
              'This device stays signed in.',
            ],
            confirmLabel: 'Sign out',
          };
    case 'others':
      return {
        title: 'Sign out every other session?',
        body: 'Every session except this one stops working straight away.',
        impacts: ['This device stays signed in.'],
        confirmLabel: 'Sign out others',
      };
    default:
      return {
        title: 'Sign out everywhere?',
        body: 'Every session stops working straight away, including this one.',
        impacts: ['You will be signed out here and returned to the sign-in page.'],
        confirmLabel: 'Sign out everywhere',
      };
  }
}

function successMessage(action: SessionAction, result: SessionRevocation): string {
  if (action.kind === 'one') {
    return `${deviceDescription(action.session.client, action.session.deviceClass)} was signed out.`;
  }
  if (result.revokedCount === 0) {
    return 'There were no other sessions to sign out.';
  }
  return result.revokedCount === 1
    ? 'One other session was signed out.'
    : `${result.revokedCount} other sessions were signed out.`;
}
