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
  /** The session the rows currently in `items` belong to. */
  private loadedGeneration = 0;

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

    // Rows already on screen belong to the session that loaded them. Signing out clears the
    // user without navigating, so without this the previous user's pinned events keep
    // rendering in the signed-out session. Declared after the first load so its replayed
    // current value is a no-op.
    this.favouritesStore.session$.pipe(takeUntil(this.destroy$)).subscribe((generation) => {
      if (generation === this.loadedGeneration) {
        return;
      }

      this.items = [];
      this.error = '';

      if (this.favouritesStore.isSignedIn) {
        this.load();
      } else {
        this.loading = false;
        this.requiresLogin = true;
      }
    });
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
    // Signing out clears the user state without navigating, so this page can outlive the
    // session it loaded for. Without this the response would render the previous user's rows
    // and seed their ids straight back into the store the sign-out just cleared.
    const generation = this.favouritesStore.sessionGeneration;
    this.loadedGeneration = generation;

    this.loading = true;
    this.error = '';
    this.requiresLogin = false;

    this.favourites
      .getMyPinned()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (items) => {
          if (!this.favouritesStore.isCurrentSession(generation)) {
            this.loading = false;
            this.items = [];
            this.requiresLogin = !this.favouritesStore.isSignedIn;
            return;
          }

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
