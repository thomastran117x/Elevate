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
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import { ConfirmDialogComponent } from '../../../../../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  Passkey,
  PasskeyList,
  PasskeyOutcome,
  PasskeysService,
} from '../../../../../../auth/services/passkeys.service';

interface Banner {
  tone: 'success' | 'error';
  text: string;
}

/** The most characters a passkey name may have; the server enforces the same limit. */
export const PASSKEY_NAME_MAX_LENGTH = 64;

/**
 * The account's passkeys: adding one on this device, renaming, and removing. It lives inside the
 * security tab's step-up gate. Every change spends the step-up proof, so a refusal for a missing
 * proof is handed to the tab, which re-locks and prompts as for any other factor.
 */
@Component({
  selector: 'app-passkeys-card',
  standalone: true,
  imports: [DatePipe, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './passkeys-card.component.html',
})
export class PasskeysCardComponent implements OnInit {
  /** Emitted when a change was refused because the session's step-up proof is missing. */
  readonly stepUpRequired = output<void>();

  readonly nameMaxLength = PASSKEY_NAME_MAX_LENGTH;
  readonly list = signal<PasskeyList | null>(null);
  readonly loading = signal(true);
  readonly adding = signal(false);
  readonly newName = signal('');
  readonly editingId = signal<number | null>(null);
  readonly editName = signal('');
  readonly editError = signal('');
  readonly busyId = signal<number | null>(null);
  readonly confirmingRemove = signal<Passkey | null>(null);
  readonly banner = signal<Banner | null>(null);

  readonly passkeys = computed<Passkey[]>(() => this.list()?.passkeys ?? []);
  readonly atLimit = computed(() => {
    const list = this.list();
    return !!list && list.limit > 0 && list.passkeys.length >= list.limit;
  });

  /** Read once: support cannot change while the page is open, and this tab renders client-side. */
  readonly supported: boolean;

  private readonly bannerRef = viewChild<ElementRef<HTMLElement>>('bannerRef');
  private readonly service = inject(PasskeysService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  constructor() {
    this.supported = this.service.isSupported();
  }

  ngOnInit(): void {
    this.load();
  }

  add(): void {
    if (this.adding() || !this.supported) {
      return;
    }

    this.banner.set(null);
    this.adding.set(true);
    this.service
      .register(this.newName().trim() || null)
      .pipe(
        finalize(() => this.adding.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (outcome) => this.handleAdded(outcome),
        error: (err) => this.handleError(err, 'Unable to add a passkey.'),
      });
  }

  startRename(passkey: Passkey): void {
    this.banner.set(null);
    this.editError.set('');
    this.editName.set(passkey.name);
    this.editingId.set(passkey.id);
    afterNextRender(() => document.getElementById(`passkey-name-${passkey.id}`)?.focus(), {
      injector: this.injector,
    });
  }

  cancelRename(): void {
    const id = this.editingId();
    this.editingId.set(null);
    this.editError.set('');
    if (id !== null) {
      afterNextRender(() => document.getElementById(`passkey-rename-${id}`)?.focus(), {
        injector: this.injector,
      });
    }
  }

  saveRename(passkey: Passkey): void {
    const name = this.editName().trim();
    if (!name) {
      this.editError.set('Give the passkey a name.');
      return;
    }
    if (name === passkey.name) {
      this.cancelRename();
      return;
    }

    this.busyId.set(passkey.id);
    this.service
      .rename(passkey.id, name)
      .pipe(
        finalize(() => this.busyId.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (renamed) => {
          this.replace(renamed);
          this.cancelRename();
          this.show('success', `Renamed to ${renamed.name}.`);
        },
        error: (err) => {
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.stepUpRequired.emit();
            return;
          }
          this.editError.set(getApiClientMessage(err, 'Unable to rename the passkey.'));
        },
      });
  }

  requestRemove(passkey: Passkey): void {
    this.banner.set(null);
    this.confirmingRemove.set(passkey);
  }

  onRemoveResolved(confirmed: boolean): void {
    const passkey = this.confirmingRemove();
    if (!confirmed || !passkey) {
      this.confirmingRemove.set(null);
      return;
    }

    this.busyId.set(passkey.id);
    this.service
      .remove(passkey.id)
      .pipe(
        finalize(() => {
          this.busyId.set(null);
          this.confirmingRemove.set(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.show('success', `${passkey.name} was removed. It can no longer sign in.`);
          this.load();
        },
        error: (err) => this.handleError(err, 'Unable to remove the passkey.'),
      });
  }

  onNameInput(event: Event): void {
    this.newName.set((event.target as HTMLInputElement).value);
  }

  onEditInput(event: Event): void {
    this.editName.set((event.target as HTMLInputElement).value);
    this.editError.set('');
  }

  private handleAdded(outcome: PasskeyOutcome<Passkey>): void {
    switch (outcome.status) {
      case 'done':
        this.newName.set('');
        this.show('success', `${outcome.value.name} was added. You can use it to sign in.`);
        this.load();
        return;
      case 'cancelled':
        this.show('error', 'Adding a passkey was cancelled. Nothing was changed.');
        return;
      case 'already_registered':
        this.show('error', 'This device already has a passkey for your account.');
        return;
      case 'unsupported':
        this.show('error', "This browser can't create passkeys.");
        return;
      default:
        this.show('error', 'Adding a passkey failed. Try again.');
    }
  }

  private load(): void {
    this.loading.set(true);
    this.service
      .list()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (list) => this.list.set(list),
        // Shown without taking focus: nothing the user did produced it.
        error: (err) =>
          this.show('error', getApiClientMessage(err, 'Unable to load your passkeys.'), false),
      });
  }

  private replace(renamed: Passkey): void {
    const list = this.list();
    if (!list) {
      return;
    }
    this.list.set({
      ...list,
      passkeys: list.passkeys.map((passkey) => (passkey.id === renamed.id ? renamed : passkey)),
    });
  }

  /** The server's message names the reason, such as the last sign-in method or the limit. */
  private handleError(err: unknown, fallback: string): void {
    if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
      this.stepUpRequired.emit();
      return;
    }
    this.show('error', getApiClientMessage(err, fallback));
  }

  /** Screen reader and keyboard users land on the result rather than on a button that moved. */
  private show(tone: Banner['tone'], text: string, focus = true): void {
    this.banner.set({ tone, text });
    if (focus) {
      afterNextRender(() => this.bannerRef()?.nativeElement.focus(), { injector: this.injector });
    }
  }
}
