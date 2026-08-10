import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { EventFavouriteToggleComponent } from '../event-favourite-toggle/event-favourite-toggle.component';
import { PinnedEvent } from '../../models/event-favourite.types';

/**
 * One row of the pinned list. Extracted so the Going and Saved sections share markup without
 * an untyped `ngTemplateOutlet` context.
 */
@Component({
  selector: 'app-pinned-event-row',
  standalone: true,
  imports: [CommonModule, RouterLink, EventFavouriteToggleComponent],
  templateUrl: './pinned-event-row.component.html',
})
export class PinnedEventRowComponent {
  @Input({ required: true }) item!: PinnedEvent;

  /** Live star state, read from the shared store by the page that owns this list. */
  @Input() favourited = false;

  @Output() readonly failed = new EventEmitter<unknown>();

  get isGoing(): boolean {
    return this.item.isRegistered;
  }

  get isCancelled(): boolean {
    return this.item.event?.lifecycleState === 'Cancelled';
  }

  /**
   * True once the star has been removed but the row is still on screen — the backtrack window
   * that lasts until the page reloads.
   */
  get isPendingRemoval(): boolean {
    return this.item.isFavourited && !this.favourited;
  }
}
