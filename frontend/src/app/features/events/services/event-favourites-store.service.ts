import { Injectable } from '@angular/core';
import { Store } from '@ngrx/store';
import {
  BehaviorSubject,
  catchError,
  finalize,
  map,
  Observable,
  shareReplay,
  throwError,
} from 'rxjs';

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
  private readonly generation$ = new BehaviorSubject<number>(0);

  /**
   * Local writes made while an id load is in flight. The response is a snapshot from before
   * they happened, so it is overlaid with these rather than replacing them — without this a
   * slow load silently undoes a toggle the user already saw succeed.
   */
  private readonly writesDuringLoad = new Map<number, boolean>();

  /**
   * Toggles whose write has not resolved yet, keyed by event id and holding a token unique to
   * the toggle that opened it. Any server snapshot that predates one of these must not
   * overwrite it — the pinned page in particular can load a list that was rendered before a
   * star pressed on the search page reached the server.
   *
   * The value is an identity token rather than the requested state so a resolving toggle can
   * only ever clear its own claim. Comparing states instead would let one toggle release
   * another's entry whenever the two happened to want the same result — including across a
   * sign-out, where the second belongs to a different user entirely.
   */
  private readonly pendingWrites = new Map<number, object>();

  readonly ids$ = this.ids.asObservable();

  /**
   * Emits the current session generation, changing whenever the signed-in user does. Pages
   * holding per-user data they already loaded should watch this and drop it — signing out
   * clears the user without navigating, so nothing else tells them to.
   */
  readonly session$ = this.generation$.asObservable();

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

  /**
   * Identifies the current signed-in session. Anything that starts an async read of per-user
   * data should capture this first and check {@link isCurrentSession} before applying the
   * result — signing out does not navigate, so a page can outlive the session it loaded for.
   */
  get sessionGeneration(): number {
    return this.generation$.value;
  }

  isCurrentSession(generation: number): boolean {
    return generation === this.generation$.value;
  }

  /** Fetches the id set once per signed-in session. No-ops for anonymous users. */
  ensureLoaded(): void {
    if (this.loaded || this.loading || this.currentUserId === null) {
      return;
    }

    const generation = this.sessionGeneration;
    this.loading = true;
    this.writesDuringLoad.clear();

    this.favourites.getMyFavouriteIds().subscribe({
      next: (ids) => {
        if (!this.isCurrentSession(generation)) {
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
        if (!this.isCurrentSession(generation)) {
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
   *
   * The write is owned by this store, not by whoever called it. A star can be pressed
   * immediately before navigating away, and if the only subscription belonged to the
   * destroyed view its teardown would cancel the in-flight request — leaving the store
   * showing a star the server never recorded.
   */
  toggle(eventId: number): Observable<boolean> {
    const generation = this.sessionGeneration;
    const wasFavourited = this.isFavourited(eventId);
    const next = !wasFavourited;

    this.apply(eventId, next);

    const claim = {};
    this.pendingWrites.set(eventId, claim);

    const request = (
      next
        ? this.favourites.favourite(eventId).pipe(map(() => true))
        : this.favourites.unfavourite(eventId).pipe(map(() => false))
    ).pipe(
      finalize(() => {
        // Only clear our own claim: a later toggle on the same event owns the entry now, and
        // after a sign-out that later toggle belongs to somebody else.
        if (this.pendingWrites.get(eventId) === claim) {
          this.pendingWrites.delete(eventId);
        }
      }),
      catchError((error: unknown) => {
        // Roll back only into the session that asked for the change. A failure that resolves
        // after a sign-out must not restore the previous user's star.
        if (this.isCurrentSession(generation)) {
          this.apply(eventId, wasFavourited);
        }

        return throwError(() => error);
      }),
      // refCount stays false so the request survives every caller unsubscribing.
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    // The subscription that keeps the write alive. Callers get an extra observer, and the
    // error is already handled here so an unobserved rejection cannot escape.
    request.subscribe({ error: () => undefined });

    return request;
  }

  /**
   * Seeds a single event's state from a server payload a page already holds.
   *
   * Skipped while a write for that event is still open: the payload was rendered before the
   * write reached the server, so applying it would undo a star the user has already seen take
   * effect and leave the store permanently opposite to the server.
   */
  setFavourited(eventId: number, favourited: boolean): void {
    if (this.pendingWrites.has(eventId)) {
      return;
    }

    this.apply(eventId, favourited);
  }

  /** Drops the cached set so the next `ensureLoaded` refetches. */
  reset(): void {
    this.loaded = false;
    this.loading = false;
    this.writesDuringLoad.clear();
    this.pendingWrites.clear();
    this.ids.next(new Set<number>());
    this.generation$.next(this.generation$.value + 1);
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
