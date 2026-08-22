import { Component, Input } from '@angular/core';

/**
 * A single shimmering placeholder bar.
 *
 * The app previously spelled `animate-pulse` placeholder divs inline at every call site;
 * this keeps the shimmer consistent and respects reduced-motion in one place.
 */
@Component({
  selector: 'app-skeleton',
  standalone: true,
  imports: [],
  template: `<span
    aria-hidden="true"
    class="block rounded bg-glass-strong motion-safe:animate-pulse"
    [class]="className"
    [style.width]="width"
    [style.height]="height"
  ></span>`,
})
export class SkeletonComponent {
  @Input() width = '100%';
  @Input() height = '0.75rem';
  @Input() className = '';
}
