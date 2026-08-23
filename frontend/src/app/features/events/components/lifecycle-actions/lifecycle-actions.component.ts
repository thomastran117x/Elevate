import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { ConfirmDialogComponent } from '../../../../shared/components/confirm-dialog/confirm-dialog.component';
import { EventLifecycleTransition, ManagedEvent } from '../../models/event.types';

/**
 * The lifecycle buttons for one event, each gated behind a confirmation that states what the
 * move will actually do.
 *
 * The button set comes from `event.availableTransitions`, which the server derives from
 * `EventLifecyclePolicy`. Nothing here knows the state machine, so a new state needs no change
 * in this component.
 */
@Component({
  selector: 'app-event-lifecycle-actions',
  standalone: true,
  imports: [ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './lifecycle-actions.component.html',
})
export class EventLifecycleActionsComponent {
  @Input({ required: true }) event!: ManagedEvent;

  /** Disables every button while a request is in flight. */
  @Input() busy = false;

  /** `compact` is the card-grid treatment: smaller buttons, no publish-readiness hint. */
  @Input() layout: 'full' | 'compact' = 'full';

  /** Emits the transition key (`pause`, `cancel`, …) once the operator has confirmed. */
  @Output() readonly act = new EventEmitter<string>();

  pending: EventLifecycleTransition | null = null;

  get transitions(): EventLifecycleTransition[] {
    return this.event?.availableTransitions ?? [];
  }

  /**
   * Whether the server says this move cannot be made yet.
   *
   * Read straight from the transition rather than derived from `publishReady`: the readiness
   * rules differ per move. A first publication must not be in the past, but resuming or
   * reinstating an event that has already started is legitimate, and gating those on
   * `publishReady` would disable exactly the recovery this feature exists to offer.
   */
  isBlocked(transition: EventLifecycleTransition): boolean {
    return !!transition.blockedReason;
  }

  blockedReason(transition: EventLifecycleTransition): string | null {
    return transition.blockedReason ? `Not ready yet: ${transition.blockedReason}` : null;
  }

  buttonClass(transition: EventLifecycleTransition): string {
    const size = this.layout === 'compact' ? 'px-3 py-2 text-xs' : 'px-5 py-3 text-sm';

    if (transition.isDestructive) {
      return `rounded-2xl border border-danger/30 text-danger ${size} font-semibold transition hover:bg-danger/10 disabled:cursor-not-allowed disabled:opacity-40`;
    }

    if (transition.target === 'Published') {
      return `rounded-2xl bg-emerald-600 text-accent-contrast ${size} font-semibold transition hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-40`;
    }

    return `rounded-2xl border border-line-strong text-muted ${size} font-semibold transition hover:bg-glass-strong disabled:cursor-not-allowed disabled:opacity-40`;
  }

  request(transition: EventLifecycleTransition): void {
    if (this.busy || this.isBlocked(transition)) {
      return;
    }

    this.pending = transition;
  }

  onResolve(confirmed: boolean): void {
    const transition = this.pending;
    this.pending = null;

    if (confirmed && transition) {
      this.act.emit(transition.key);
    }
  }
}
