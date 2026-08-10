import { Component, EventEmitter, Input, Output } from '@angular/core';

type Size = 'sm' | 'md';

/**
 * The star toggle shown on every surface that lists an event.
 *
 * It swallows the click itself rather than leaving that to each caller: the search result card
 * wraps the whole tile in a navigation handler, so an unguarded star press would also open the
 * event.
 */
@Component({
  selector: 'favourite-star',
  standalone: true,
  imports: [],
  templateUrl: './favourite-star.component.html',
})
export class FavouriteStarComponent {
  @Input() favourited = false;
  @Input() pending = false;
  @Input() disabled = false;
  @Input() size: Size = 'md';
  /** Overrides the default labels, e.g. "Save this event" on the detail page. */
  @Input() label: string | null = null;

  @Output() toggled = new EventEmitter<void>();

  get buttonClass(): string {
    const base =
      'inline-flex items-center justify-center rounded-full border transition ' +
      'disabled:cursor-not-allowed disabled:opacity-50';
    const sizing = this.size === 'sm' ? 'h-8 w-8' : 'h-10 w-10';
    const tone = this.favourited
      ? 'border-accent/40 bg-accent/15 text-accent'
      : 'border-line bg-surface/80 text-muted hover:border-line-strong hover:text-content';

    return `${base} ${sizing} ${tone}`;
  }

  get iconClass(): string {
    return this.size === 'sm' ? 'h-4 w-4' : 'h-5 w-5';
  }

  get accessibleLabel(): string {
    if (this.label) {
      return this.label;
    }

    return this.favourited ? 'Remove from saved events' : 'Save event for later';
  }

  activate(event: Event): void {
    // The card behind this button navigates on click, and Enter on a button would also
    // trigger the card's keyup handler.
    event.stopPropagation();
    event.preventDefault();

    if (this.pending || this.disabled) {
      return;
    }

    this.toggled.emit();
  }
}
