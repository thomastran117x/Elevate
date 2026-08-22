import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  ViewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * Confirms an action that is hard or impossible to take back, spelling out what it will do
 * before it happens.
 *
 * A plain overlay rather than a dialog library, matching `occurrence-scope-dialog` — the
 * codebase has no dialog dependency. Unlike that one this traps focus, closes on Escape, and
 * closes on a backdrop click, because it guards destructive actions and a dialog you cannot
 * dismiss with the keyboard is its own hazard.
 */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-dialog.component.html',
})
export class ConfirmDialogComponent {
  @Input() open = false;

  @Input() title = 'Are you sure?';

  /** Optional lead paragraph shown above the impact list. */
  @Input() body = '';

  /** Concrete consequences, one per line. Supplied by the server for lifecycle actions. */
  @Input() impacts: string[] = [];

  /** Reassurance that the action can be walked back, or null when it genuinely cannot. */
  @Input() reversibleNote: string | null = null;

  @Input() confirmLabel = 'Confirm';

  @Input() cancelLabel = 'Cancel';

  /** `danger` paints the confirm button red and adds the warning treatment. */
  @Input() tone: 'danger' | 'primary' = 'primary';

  /**
   * When set, the confirm button stays disabled until the operator types this exact string.
   * Reserved for the genuinely unrecoverable — hard deletion, not a reversible cancel.
   */
  @Input() requireTypedConfirmation: string | null = null;

  @Input() busy = false;

  /** True to proceed, false when the operator backed out. */
  @Output() readonly resolve = new EventEmitter<boolean>();

  @ViewChild('cancelButton') cancelButton?: ElementRef<HTMLButtonElement>;

  typedConfirmation = '';

  get confirmationSatisfied(): boolean {
    if (!this.requireTypedConfirmation) {
      return true;
    }

    return this.typedConfirmation.trim() === this.requireTypedConfirmation.trim();
  }

  get canConfirm(): boolean {
    return !this.busy && this.confirmationSatisfied;
  }

  confirm(): void {
    if (!this.canConfirm) {
      return;
    }

    this.typedConfirmation = '';
    this.resolve.emit(true);
  }

  dismiss(): void {
    if (this.busy) {
      return;
    }

    this.typedConfirmation = '';
    this.resolve.emit(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open) {
      this.dismiss();
    }
  }

  /** Backdrop clicks dismiss; clicks that bubble up from the panel itself must not. */
  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.dismiss();
    }
  }
}
