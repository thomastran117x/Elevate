import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { FavouriteStarComponent } from '@common/favourite-star/favourite-star.component';
import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { EventFavouritesStore } from '../../services/event-favourites-store.service';

/**
 * Drop-in star for any surface that lists an event. Owns the parts every surface needs
 * identically — the store subscription, the in-flight guard, and sending signed-out users to
 * login — so pages only supply an event id.
 *
 * Pages that need to *read* star state (the pinned page renders a "removed" hint) still read it
 * from {@link EventFavouritesStore}; this component is write-side only.
 */
@Component({
  selector: 'app-event-favourite-toggle',
  standalone: true,
  imports: [FavouriteStarComponent],
  templateUrl: './event-favourite-toggle.component.html',
})
export class EventFavouriteToggleComponent implements OnInit, OnChanges, OnDestroy {
  @Input({ required: true }) eventId!: number;
  @Input() size: 'sm' | 'md' = 'md';
  @Input() label: string | null = null;

  /** Emits the API error when a toggle fails; the star has already reverted by then. */
  @Output() readonly failed = new EventEmitter<unknown>();

  favourited = false;
  pending = false;

  private readonly destroy$ = new Subject<void>();
  /** Ends the previous star subscription when the row is recycled onto another event. */
  private readonly rewatch$ = new Subject<void>();
  private watching: number | null = null;

  constructor(
    private favouritesStore: EventFavouritesStore,
    private router: Router,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit(): void {
    this.favouritesStore.ensureLoaded();
    this.watch();
  }

  ngOnChanges(): void {
    this.watch();
  }

  ngOnDestroy(): void {
    this.rewatch$.next();
    this.rewatch$.complete();
    this.destroy$.next();
    this.destroy$.complete();
  }

  toggle(): void {
    if (!this.favouritesStore.isSignedIn) {
      this.authReturnUrl.set(this.router.url);
      void this.router.navigate(['/auth/login'], {
        queryParams: { returnUrl: this.router.url },
      });
      return;
    }

    if (this.pending) return;
    this.pending = true;

    // takeUntil only detaches this component's observer: EventFavouritesStore owns the
    // subscription that keeps the write alive, so navigating away mid-flight cannot cancel it.
    this.favouritesStore
      .toggle(this.eventId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => (this.pending = false),
        error: (error: unknown) => {
          this.pending = false;
          this.failed.emit(error);
        },
      });
  }

  private watch(): void {
    if (this.eventId === this.watching) return;
    this.watching = this.eventId;
    this.rewatch$.next();

    this.favouritesStore
      .isFavourited$(this.eventId)
      .pipe(takeUntil(this.rewatch$), takeUntil(this.destroy$))
      .subscribe((favourited) => (this.favourited = favourited));
  }
}
