import { Injectable } from '@angular/core';
import { Store } from '@ngrx/store';
import { BehaviorSubject, catchError, EMPTY, map, Observable, throwError } from 'rxjs';

import { UserState } from '../../../core/stores/user.reducer';
import { selectUser } from '../../../core/stores/user.selectors';
import { EventFavouritesService } from './event-favourites.service';

/**
 * The single source of star state for every surface that shows one (search cards, event
 * detail, the pinned page, my-waitlists, my-invites). Plain RxJS rather than NgRx, matching
 * how the rest of the feature code holds state — there are no effects anywhere in this app.
 *
 * Deliberately holds only the id set, never the event rows. List pages keep their own loaded
 * snapshot and read fill state from here, which is what lets a user unstar a row on the pinned
 * page and change their mind: the star hollows, the row stays until the page loads again.
 */
@Injectable({ providedIn: 'root' })
export class EventFavouritesStore {
  private readonly ids = new BehaviorSubject<ReadonlySet<number>>(new Set<number>());
  private loaded = false;
  private loading = false;
  private currentUserId: number | null = null;

  readonly ids$ = this.ids.asObservable();

  constructor(
    private favourites: EventFavouritesService,
    private store: Store<{ user: UserState }>,
  ) {
    this.store.select(selectUser).subscribe((user) => {
      const userId = user?.Id ?? null;
      if (userId === this.currentUserId) {
        return;
      }

      // Signing out or switching accounts in the same tab must not leave the previous
      // user's stars on screen.
      this.currentUserId = userId;
      this.reset();
    });
  }

  /** Whether there is a signed-in user to star anything on behalf of. */
  get isSignedIn(): boolean {
    return this.currentUserId !== null;
  }

  /** Fetches the id set once per signed-in session. No-ops for anonymous users. */
  ensureLoaded(): void {
    if (this.loaded || this.loading || this.currentUserId === null) {
      return;
    }

    this.loading = true;
    this.favourites
      .getMyFavouriteIds()
      .pipe(
        catchError(() => {
          // A failed load just leaves every star hollow; the toggle still works.
          this.loading = false;
          return EMPTY;
        }),
      )
      .subscribe((ids) => {
        this.loading = false;
        this.loaded = true;
        this.ids.next(new Set(ids));
      });
  }

  isFavourited(eventId: number): boolean {
    return this.ids.value.has(eventId);
  }

  /**
   * Flips the star optimistically and writes through, reverting if the write fails.
   * Emits the state the star ended up in.
   */
  toggle(eventId: number): Observable<boolean> {
    const wasFavourited = this.isFavourited(eventId);
    const next = !wasFavourited;

    this.apply(eventId, next);

    const request = next
      ? this.favourites.favourite(eventId).pipe(map(() => true))
      : this.favourites.unfavourite(eventId).pipe(map(() => false));

    return request.pipe(
      catchError((error: unknown) => {
        this.apply(eventId, wasFavourited);
        return throwError(() => error);
      }),
    );
  }

  /** Seeds a single event's state, for pages that already know it (e.g. event detail). */
  setFavourited(eventId: number, favourited: boolean): void {
    this.apply(eventId, favourited);
  }

  /** Drops the cached set so the next `ensureLoaded` refetches. */
  reset(): void {
    this.loaded = false;
    this.loading = false;
    this.ids.next(new Set<number>());
  }

  /** Emits whether this specific event is starred. */
  isFavourited$(eventId: number): Observable<boolean> {
    return this.ids$.pipe(map((ids) => ids.has(eventId)));
  }

  private apply(eventId: number, favourited: boolean): void {
    const next = new Set(this.ids.value);
    if (favourited) {
      next.add(eventId);
    } else {
      next.delete(eventId);
    }
    this.ids.next(next);
  }
}
