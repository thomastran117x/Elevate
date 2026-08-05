import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { OccurrenceEditScope } from '../../models/event.types';

/**
 * Asks whether an edit applies to one occurrence or to this and every later one.
 *
 * A plain overlay rather than a dialog library, because the codebase has no dialog dependency
 * and this is the only modal in the events feature.
 */
@Component({
  selector: 'app-occurrence-scope-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './occurrence-scope-dialog.component.html',
})
export class OccurrenceScopeDialogComponent {
  @Input() open = false;

  /** Human label for the occurrence being edited, e.g. "Occurrence 3 of 8". */
  @Input() occurrenceLabel = '';

  @Input() saving = false;

  /** Emits the chosen scope, or null when the organizer backs out. */
  @Output() readonly choose = new EventEmitter<OccurrenceEditScope | null>();

  select(scope: OccurrenceEditScope): void {
    this.choose.emit(scope);
  }

  dismiss(): void {
    this.choose.emit(null);
  }
}
