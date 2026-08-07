import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { PinnedEventRowComponent } from '../../components/pinned-event-row/pinned-event-row.component';
import { PinnedEvent } from '../../models/event-favourite.types';
import { EventFavouritesService } from '../../services/event-favourites.service';
import { EventFavouritesStore } from '../../services/event-favourites-store.service';

@Component({
  selector: 'app-my-pinned',
  standalone: true,
  imports: [CommonModule, RouterLink, PinnedEventRowComponent],
  templateUrl: './my-pinned.component.html',
})
export class MyPinnedComponent implements OnInit, OnDestroy {
  /**
   * A snapshot loaded once. Toggling a star deliberately does NOT re-filter or reload it —
   * unstarring hollows the star and leaves the row in place so the user can change their mind,
   * and the row only disappears the next time this page loads.
   */
  items: PinnedEvent[] = [];
  favouritedIds: ReadonlySet<number> = new Set<number>();
  loading = true;
  error = '';
  requiresLogin = false;

  private readonly destroy$ = new Subject<void>();

  constructor(
    private favourites: EventFavouritesService,
    private favouritesStore: EventFavouritesStore,
    private router: Router,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit(): void {
    this.favouritesStore.ids$
      .pipe(takeUntil(this.destroy$))
      .subscribe((ids) => (this.favouritedIds = ids));

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get going(): PinnedEvent[] {
    return this.items.filter((item) => item.isRegistered);
  }

  get saved(): PinnedEvent[] {
    return this.items.filter((item) => !item.isRegistered);
  }

  isFavourited(item: PinnedEvent): boolean {
    return this.favouritedIds.has(item.event.id);
  }

  goToLogin(): void {
    this.authReturnUrl.set(this.router.url);
    void this.router.navigate(['/auth/login'], {
      queryParams: { returnUrl: this.router.url },
    });
  }

  /**
   * Note what is deliberately absent: no reload on success, unlike
   * `MyWaitlistsComponent.leave()`. Unstarring leaves the row on screen so the star is its own
   * undo; the row only disappears the next time this page loads.
   */
  onFavouriteFailed(response: unknown): void {
    this.error = getApiClientMessage(response, 'We could not update this event.');
  }

  private load(): void {
    this.loading = true;
    this.error = '';

    this.favourites
      .getMyPinned()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (items) => {
          this.items = items;
          this.loading = false;
          // Seed the shared set from the rows we just loaded so the stars render correctly
          // even if this is the first page the user landed on.
          for (const item of items) {
            this.favouritesStore.setFavourited(item.event.id, item.isFavourited);
          }
        },
        error: (response) => {
          this.loading = false;
          this.requiresLogin = response?.status === 401;
          if (!this.requiresLogin) {
            this.error = getApiClientMessage(response, 'We could not load your pinned events.');
          }
        },
      });
  }
}
