import { DatePipe, isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  PLATFORM_ID,
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
import {
  AuthService,
  RecoveryCodeStatusResponse,
} from '../../../../../../auth/services/auth.service';
import { downloadFile } from '../../../../../../../shared/download/download-file';
import { PendingRecoveryCodes } from './pending-recovery-codes';

/** At or below this many unused codes the card asks the user to make a new set. */
export const RECOVERY_CODES_LOW_THRESHOLD = 3;

export const RECOVERY_CODES_FILE_NAME = 'eventxperience-recovery-codes.txt';

type CopyState = 'idle' | 'copied' | 'failed';
type StatusLevel = 'none' | 'ok' | 'low' | 'empty';

/**
 * Status and generation of single-use recovery codes. A new set is shown exactly once, here, and
 * cleared from memory when the user confirms they saved it; the server never returns it again.
 *
 * It lives inside the security tab's step-up gate. Generation spends the step-up proof, so a refusal
 * for a missing proof is handed to the tab, which re-locks and prompts as for any other factor. The
 * unsaved set is held by {@link PendingRecoveryCodes}, which the tab provides, so re-locking the tab
 * hides the codes until the user verifies again rather than losing them.
 */
@Component({
  selector: 'app-recovery-codes-card',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recovery-codes-card.component.html',
})
export class RecoveryCodesCardComponent implements OnInit {
  /** Emitted when generation was refused because the session's step-up proof is missing. */
  readonly stepUpRequired = output<void>();

  readonly status = signal<RecoveryCodeStatusResponse | null>(null);
  readonly loading = signal(true);
  readonly confirming = signal(false);
  readonly generating = signal(false);
  private readonly pending = inject(PendingRecoveryCodes);

  readonly codes = this.pending.codes;
  readonly acknowledged = this.pending.acknowledged;
  readonly copyState = signal<CopyState>('idle');
  readonly error = signal('');
  readonly notice = signal('');

  readonly remaining = computed(() => this.status()?.remainingCount ?? 0);
  readonly total = computed(() => this.status()?.totalCount ?? 10);
  readonly hasSet = computed(() => !!this.status()?.generatedAtUtc);
  readonly level = computed<StatusLevel>(() => {
    if (!this.hasSet()) {
      return 'none';
    }
    const remaining = this.remaining();
    return remaining === 0 ? 'empty' : remaining <= RECOVERY_CODES_LOW_THRESHOLD ? 'low' : 'ok';
  });

  private readonly codesHeading = viewChild<ElementRef<HTMLElement>>('codesHeading');
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  ngOnInit(): void {
    this.loadStatus();
  }

  /** A first set is made straight away; replacing one asks first, since it voids the old codes. */
  requestGeneration(): void {
    this.error.set('');
    this.notice.set('');
    if (this.hasSet()) {
      this.confirming.set(true);
      return;
    }
    this.generate();
  }

  cancelGeneration(): void {
    this.confirming.set(false);
  }

  generate(): void {
    if (this.generating()) {
      return;
    }

    this.error.set('');
    this.generating.set(true);

    // Deliberately not cut short when the card is destroyed: the response is the only copy of the
    // new codes, so it must reach the tab-scoped holder even if the tab re-locks meanwhile. A
    // single HTTP response completes the subscription on its own.
    this.auth
      .regenerateRecoveryCodes()
      .pipe(finalize(() => this.generating.set(false)))
      .subscribe({
        next: (result) => {
          this.confirming.set(false);
          this.pending.show(result.codes);
          this.copyState.set('idle');
          this.status.set({
            remainingCount: result.codes.length,
            totalCount: result.codes.length,
            generatedAtUtc: result.generatedAtUtc,
          });
          // Screen reader and keyboard users land on the new codes, not on a button that moved.
          afterNextRender(() => this.codesHeading()?.nativeElement.focus(), {
            injector: this.injector,
          });
        },
        error: (err) => {
          this.confirming.set(false);
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.stepUpRequired.emit();
            return;
          }
          this.error.set(getApiClientMessage(err, 'Unable to generate recovery codes.'));
        },
      });
  }

  copyAll(): void {
    const codes = this.codes();
    if (!codes || typeof navigator === 'undefined' || !navigator.clipboard) {
      this.copyState.set('failed');
      return;
    }

    navigator.clipboard.writeText(codes.join('\n')).then(
      () => this.copyState.set('copied'),
      () => this.copyState.set('failed'),
    );
  }

  download(): void {
    const codes = this.codes();
    if (!codes || !this.isBrowser) {
      return;
    }

    const generatedAt = this.status()?.generatedAtUtc ?? '';
    const text = [
      'EventXperience recovery codes',
      `Generated: ${generatedAt}`,
      'Each code can be used once. Keep this file somewhere private.',
      '',
      ...codes.map((code, index) => `${index + 1}. ${code}`),
      '',
    ].join('\n');

    downloadFile(text, RECOVERY_CODES_FILE_NAME, 'text/plain;charset=utf-8');
  }

  setAcknowledged(value: boolean): void {
    this.acknowledged.set(value);
  }

  /** Forgets the codes; there is no way to show them again. */
  finish(): void {
    if (!this.acknowledged()) {
      return;
    }
    this.pending.clear();
    this.copyState.set('idle');
    this.notice.set('Your recovery codes are ready. Each one works once.');
  }

  private loadStatus(): void {
    this.loading.set(true);
    this.auth
      .getRecoveryCodeStatus()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (status) => this.status.set(status),
        error: (err) =>
          this.error.set(getApiClientMessage(err, 'Unable to load your recovery code status.')),
      });
  }
}
