import { Injectable } from '@angular/core';
import { Store } from '@ngrx/store';
import { BehaviorSubject, catchError, map, Observable, throwError } from 'rxjs';

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

  /**
   * Bumped by every reset. An in-flight id load carries the generation it started under and
   * throws its response away if that no longer matches — otherwise a slow GET issued for the
   * previous user lands after a sign-out and repopulates their stars, marking the store loaded
   * so the new user never fetches their own.
   */
  private generation = 0;

  /**
   * Local writes made while an id load is in flight. The response is a snapshot from before
   * they happened, so it is overlaid with these rather than replacing them — without this a
   * slow load silently undoes a toggle the user already saw succeed.
   */
  private readonly writesDuringLoad = new Map<number, boolean>();

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

    const generation = this.generation;
    this.loading = true;
    this.writesDuringLoad.clear();

    this.favourites.getMyFavouriteIds().subscribe({
      next: (ids) => {
        if (generation !== this.generation) {
          return;
        }

        this.loading = false;
        this.loaded = true;

        const next = new Set(ids);
        for (const [eventId, favourited] of this.writesDuringLoad) {
          if (favourited) {
            next.add(eventId);
          } else {
            next.delete(eventId);
          }
        }
        this.writesDuringLoad.clear();

        this.ids.next(next);
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }

        // A failed load just leaves every star hollow; the toggle still works.
        this.loading = false;
        this.writesDuringLoad.clear();
      },
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
    this.generation += 1;
    this.loaded = false;
    this.loading = false;
    this.writesDuringLoad.clear();
    this.ids.next(new Set<number>());
  }

  /** Emits whether this specific event is starred. */
  isFavourited$(eventId: number): Observable<boolean> {
    return this.ids$.pipe(map((ids) => ids.has(eventId)));
  }

  private apply(eventId: number, favourited: boolean): void {
    if (this.loading) {
      this.writesDuringLoad.set(eventId, favourited);
    }

    const next = new Set(this.ids.value);
    if (favourited) {
      next.add(eventId);
    } else {
      next.delete(eventId);
    }
    this.ids.next(next);
  }
}
