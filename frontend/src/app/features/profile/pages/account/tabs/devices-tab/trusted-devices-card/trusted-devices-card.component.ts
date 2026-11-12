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
import { Observable } from 'rxjs';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import { ConfirmDialogComponent } from '../../../../../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  AccountDevicesService,
  TrustedDevice,
  TrustedDeviceRevocation,
} from '../../../../../services/account-devices.service';
import { MfaGateComponent } from '../../../mfa-gate/mfa-gate.component';
import { deviceDescription } from '../device-labels';
import { PendingStepUp } from '../pending-step-up';

type DeviceAction = { kind: 'one'; device: TrustedDevice } | { kind: 'all' };

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
 * The devices that skip sign-in verification, and forgetting one or all of them. Forgetting never
 * signs anything out; the device is simply asked to verify at its next sign-in. Like the sessions
 * card, a refusal for a missing step-up opens verification and then finishes.
 */
@Component({
  selector: 'app-trusted-devices-card',
  standalone: true,
  imports: [DatePipe, ConfirmDialogComponent, MfaGateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trusted-devices-card.component.html',
})
export class TrustedDevicesCardComponent implements OnInit {
  readonly devices = signal<TrustedDevice[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly confirming = signal<DeviceAction | null>(null);
  readonly busy = signal(false);
  readonly banner = signal<Banner | null>(null);
  readonly stepUp = new PendingStepUp();

  readonly confirmCopy = computed(() => {
    const action = this.confirming();
    return action ? confirmCopyFor(action) : null;
  });

  private readonly bannerRef = viewChild<ElementRef<HTMLElement>>('bannerRef');
  private readonly accountDevices = inject(AccountDevicesService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  ngOnInit(): void {
    this.load();
  }

  describe(device: TrustedDevice): string {
    return deviceDescription(device.client, device.deviceClass);
  }

  request(action: DeviceAction): void {
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
    this.accountDevices
      .listTrustedDevices()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (devices) => this.devices.set(devices),
        error: (err) =>
          this.loadError.set(getApiClientMessage(err, 'Unable to load your trusted devices.')),
      });
  }

  private run(action: DeviceAction): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.forget(action)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.confirming.set(null);
          this.show('success', successMessage(action, result));
          this.load();
        },
        error: (err) => {
          this.confirming.set(null);
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.stepUp.request(() => this.run(action));
            return;
          }
          this.show(
            'error',
            getApiClientMessage(err, 'Unable to forget the device. Please try again.'),
          );
          this.load();
        },
      });
  }

  private forget(action: DeviceAction): Observable<TrustedDeviceRevocation> {
    return action.kind === 'one'
      ? this.accountDevices.revokeTrustedDevice(action.device.id)
      : this.accountDevices.revokeAllTrustedDevices();
  }

  /** Screen reader and keyboard users land on the result rather than on a button that moved. */
  private show(tone: Banner['tone'], text: string): void {
    this.banner.set({ tone, text });
    afterNextRender(() => this.bannerRef()?.nativeElement.focus(), { injector: this.injector });
  }
}

function confirmCopyFor(action: DeviceAction): ConfirmCopy {
  if (action.kind === 'all') {
    return {
      title: 'Forget every trusted device?',
      body: 'Each device, this one included, will be asked to verify the next time it signs in.',
      impacts: ['No one is signed out. To end sessions, use the Sessions list above.'],
      confirmLabel: 'Forget all',
    };
  }

  const name = deviceDescription(action.device.client, action.device.deviceClass);
  return {
    title: action.device.current ? 'Forget this device?' : `Forget ${name}?`,
    body: action.device.current
      ? 'This browser will be asked to verify the next time you sign in on it.'
      : 'That device will be asked to verify the next time it signs in.',
    impacts: ['No one is signed out. To end a session, use the Sessions list above.'],
    confirmLabel: 'Forget device',
  };
}

function successMessage(action: DeviceAction, result: TrustedDeviceRevocation): string {
  if (action.kind === 'one') {
    return result.currentDeviceRevoked
      ? 'This device was forgotten. You will be asked to verify next time you sign in here.'
      : `${deviceDescription(action.device.client, action.device.deviceClass)} was forgotten.`;
  }
  if (result.revokedCount === 0) {
    return 'There were no trusted devices to forget.';
  }
  return result.revokedCount === 1
    ? 'One trusted device was forgotten.'
    : `${result.revokedCount} trusted devices were forgotten.`;
}
