import { TestBed } from '@angular/core/testing';
import { MockStore } from '@ngrx/store/testing';
import { NEVER, of, Subject, throwError } from 'rxjs';

import { makeCurrentUser, provideTestStore } from '@testing';

import { selectUser } from '../../../core/stores/user.selectors';
import { EventFavouritesService } from './event-favourites.service';
import { EventFavouritesStore } from './event-favourites-store.service';

describe('EventFavouritesStore', () => {
  let favourites: jasmine.SpyObj<EventFavouritesService>;

  function setup(signedIn = true): { store: EventFavouritesStore; mockStore: MockStore } {
    favourites = jasmine.createSpyObj<EventFavouritesService>('EventFavouritesService', [
      'getMyFavouriteIds',
      'favourite',
      'unfavourite',
    ]);
    favourites.getMyFavouriteIds.and.returnValue(of([12, 47]));
    favourites.favourite.and.returnValue(
      of({ eventId: 0, isFavourited: true, favouritedAtUtc: null }),
    );
    favourites.unfavourite.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      providers: [
        EventFavouritesStore,
        { provide: EventFavouritesService, useValue: favourites },
        ...provideTestStore({ user: signedIn ? makeCurrentUser({ Id: 5 }) : null }),
      ],
    });

    return {
      store: TestBed.inject(EventFavouritesStore),
      mockStore: TestBed.inject(MockStore),
    };
  }

  it('loads the id set once and reuses it', () => {
    const { store } = setup();

    store.ensureLoaded();
    store.ensureLoaded();

    expect(favourites.getMyFavouriteIds).toHaveBeenCalledTimes(1);
    expect(store.isFavourited(12)).toBeTrue();
    expect(store.isFavourited(99)).toBeFalse();
  });

  it('does not call the API for anonymous visitors', () => {
    const { store } = setup(false);

    store.ensureLoaded();

    expect(favourites.getMyFavouriteIds).not.toHaveBeenCalled();
    expect(store.isSignedIn).toBeFalse();
  });

  it('leaves every star hollow when the load fails', () => {
    const { store } = setup();
    favourites.getMyFavouriteIds.and.returnValue(throwError(() => new Error('offline')));

    store.ensureLoaded();

    expect(store.isFavourited(12)).toBeFalse();
  });

  it('flips the star optimistically before the write resolves', () => {
    const { store } = setup();
    store.ensureLoaded();

    // Never emits, so the only state change observable here is the optimistic one.
    favourites.favourite.and.returnValue(NEVER);
    store.toggle(99).subscribe();

    expect(store.isFavourited(99)).toBeTrue();
    expect(favourites.favourite).toHaveBeenCalledWith(99);
  });

  it('reverts the star when the write fails', () => {
    const { store } = setup();
    store.ensureLoaded();
    favourites.unfavourite.and.returnValue(throwError(() => new Error('500')));

    let errored = false;
    store.toggle(12).subscribe({ error: () => (errored = true) });

    expect(errored).toBeTrue();
    expect(store.isFavourited(12)).toBeTrue();
  });

  it('unstars and restars the same event without reloading', () => {
    const { store } = setup();
    store.ensureLoaded();

    store.toggle(12).subscribe();
    expect(store.isFavourited(12)).toBeFalse();

    // The backtrack the pinned page depends on: the row is still rendered, so the very next
    // click must re-star it.
    store.toggle(12).subscribe();
    expect(store.isFavourited(12)).toBeTrue();

    expect(favourites.unfavourite).toHaveBeenCalledOnceWith(12);
    expect(favourites.favourite).toHaveBeenCalledOnceWith(12);
    expect(favourites.getMyFavouriteIds).toHaveBeenCalledTimes(1);
  });

  it('clears the set when the signed-in user changes', () => {
    const { store, mockStore } = setup();
    store.ensureLoaded();
    expect(store.isFavourited(12)).toBeTrue();

    mockStore.overrideSelector(selectUser, makeCurrentUser({ Id: 6 }));
    mockStore.refreshState();

    // One user's stars must not carry into the next session in the same tab.
    expect(store.isFavourited(12)).toBeFalse();

    store.ensureLoaded();
    expect(favourites.getMyFavouriteIds).toHaveBeenCalledTimes(2);
  });

  it('clears the set and stops loading on sign-out', () => {
    const { store, mockStore } = setup();
    store.ensureLoaded();

    mockStore.overrideSelector(selectUser, null);
    mockStore.refreshState();

    expect(store.isFavourited(12)).toBeFalse();
    expect(store.isSignedIn).toBeFalse();

    store.ensureLoaded();
    expect(favourites.getMyFavouriteIds).toHaveBeenCalledTimes(1);
  });

  it('seeds a single event without touching the rest', () => {
    const { store } = setup();
    store.ensureLoaded();

    store.setFavourited(99, true);
    store.setFavourited(12, false);

    expect(store.isFavourited(99)).toBeTrue();
    expect(store.isFavourited(12)).toBeFalse();
    expect(store.isFavourited(47)).toBeTrue();
  });

  describe('races against an in-flight id load', () => {
    it('keeps a toggle the user already saw succeed', () => {
      const { store } = setup();
      const ids$ = new Subject<number[]>();
      favourites.getMyFavouriteIds.and.returnValue(ids$.asObservable());

      store.ensureLoaded();
      store.toggle(99).subscribe();
      expect(store.isFavourited(99)).toBeTrue();

      // The response is a snapshot from before the toggle, so it must not undo it.
      ids$.next([12, 47]);
      ids$.complete();

      expect(store.isFavourited(99)).toBeTrue();
      expect(store.isFavourited(12)).toBeTrue();
    });

    it('keeps an unstar the user already saw succeed', () => {
      const { store } = setup();
      const ids$ = new Subject<number[]>();
      favourites.getMyFavouriteIds.and.returnValue(ids$.asObservable());

      store.ensureLoaded();
      store.setFavourited(12, true);
      store.toggle(12).subscribe();
      expect(store.isFavourited(12)).toBeFalse();

      ids$.next([12, 47]);
      ids$.complete();

      expect(store.isFavourited(12)).toBeFalse();
      expect(store.isFavourited(47)).toBeTrue();
    });

    it('discards a response that lands after a sign-out', () => {
      const { store, mockStore } = setup();
      const ids$ = new Subject<number[]>();
      favourites.getMyFavouriteIds.and.returnValue(ids$.asObservable());

      store.ensureLoaded();

      mockStore.overrideSelector(selectUser, null);
      mockStore.refreshState();

      // The previous user's stars must not reappear in a signed-out session.
      ids$.next([12, 47]);
      ids$.complete();

      expect(store.isFavourited(12)).toBeFalse();
    });

    it('still fetches for the next user when the previous response lands late', () => {
      const { store, mockStore } = setup();
      const ids$ = new Subject<number[]>();
      favourites.getMyFavouriteIds.and.returnValue(ids$.asObservable());

      store.ensureLoaded();

      mockStore.overrideSelector(selectUser, makeCurrentUser({ Id: 6 }));
      mockStore.refreshState();

      ids$.next([12, 47]);
      ids$.complete();

      // A stale response must not mark the store loaded, or user 6 never gets their own set.
      favourites.getMyFavouriteIds.and.returnValue(of([88]));
      store.ensureLoaded();

      expect(favourites.getMyFavouriteIds).toHaveBeenCalledTimes(2);
      expect(store.isFavourited(88)).toBeTrue();
      expect(store.isFavourited(12)).toBeFalse();
    });

    it('retries after a failed load', () => {
      const { store } = setup();
      favourites.getMyFavouriteIds.and.returnValue(throwError(() => new Error('offline')));

      store.ensureLoaded();

      favourites.getMyFavouriteIds.and.returnValue(of([12]));
      store.ensureLoaded();

      expect(store.isFavourited(12)).toBeTrue();
    });
  });

  describe('writes outliving the caller', () => {
    it('keeps the request alive when every caller unsubscribes', () => {
      const { store } = setup();
      const write$ = new Subject<{
        eventId: number;
        isFavourited: boolean;
        favouritedAtUtc: null;
      }>();
      favourites.favourite.and.returnValue(write$.asObservable());

      // A star pressed just before navigating away: the view's subscription goes, the
      // in-flight write must not.
      const subscription = store.toggle(99).subscribe();
      subscription.unsubscribe();

      expect(write$.observed).toBeTrue();
    });

    it('still rolls back after the caller unsubscribes', () => {
      const { store } = setup();
      const write$ = new Subject<void>();
      favourites.unfavourite.and.returnValue(write$.asObservable());
      store.setFavourited(12, true);

      store
        .toggle(12)
        .subscribe({ error: () => undefined })
        .unsubscribe();
      expect(store.isFavourited(12)).toBeFalse();

      write$.error(new Error('500'));

      expect(store.isFavourited(12)).toBeTrue();
    });

    it('does not resurrect a star when the rollback lands after sign-out', () => {
      const { store, mockStore } = setup();
      const write$ = new Subject<void>();
      favourites.unfavourite.and.returnValue(write$.asObservable());
      store.setFavourited(12, true);

      store.toggle(12).subscribe({ error: () => undefined });

      mockStore.overrideSelector(selectUser, null);
      mockStore.refreshState();

      write$.error(new Error('500'));

      expect(store.isFavourited(12)).toBeFalse();
    });
  });

  describe('seeding against an open write', () => {
    it('does not let a stale page payload undo an in-flight toggle', () => {
      const { store } = setup();
      const write$ = new Subject<{
        eventId: number;
        isFavourited: boolean;
        favouritedAtUtc: null;
      }>();
      favourites.favourite.and.returnValue(write$.asObservable());

      // Starred on the search page, then straight to the pinned page whose payload was
      // rendered before the POST landed.
      store.toggle(99).subscribe();
      store.setFavourited(99, false);

      expect(store.isFavourited(99)).toBeTrue();

      write$.next({ eventId: 99, isFavourited: true, favouritedAtUtc: null });
      write$.complete();

      expect(store.isFavourited(99)).toBeTrue();
    });

    it('accepts seeding again once the write resolves', () => {
      const { store } = setup();
      const write$ = new Subject<{
        eventId: number;
        isFavourited: boolean;
        favouritedAtUtc: null;
      }>();
      favourites.favourite.and.returnValue(write$.asObservable());

      store.toggle(99).subscribe();
      write$.next({ eventId: 99, isFavourited: true, favouritedAtUtc: null });
      write$.complete();

      store.setFavourited(99, false);

      expect(store.isFavourited(99)).toBeFalse();
    });

    it('accepts seeding again after a failed write', () => {
      const { store } = setup();
      const write$ = new Subject<never>();
      favourites.favourite.and.returnValue(write$.asObservable());

      store.toggle(99).subscribe({ error: () => undefined });
      write$.error(new Error('500'));

      store.setFavourited(99, true);

      expect(store.isFavourited(99)).toBeTrue();
    });

    it('does not let a resolving toggle release a later one for the same event', () => {
      const { store, mockStore } = setup();
      const first$ = new Subject<{
        eventId: number;
        isFavourited: boolean;
        favouritedAtUtc: null;
      }>();
      favourites.favourite.and.returnValue(first$.asObservable());

      store.toggle(99).subscribe();

      // A different account opens the same transition on the same event.
      mockStore.overrideSelector(selectUser, makeCurrentUser({ Id: 6 }));
      mockStore.refreshState();
      favourites.favourite.and.returnValue(NEVER);
      store.toggle(99).subscribe();

      // The first account's write finally resolves. Both wanted `true`, so a state comparison
      // would hand it the second account's claim.
      first$.next({ eventId: 99, isFavourited: true, favouritedAtUtc: null });
      first$.complete();

      store.setFavourited(99, false);

      expect(store.isFavourited(99)).toBeTrue();
    });

    it('leaves other events seedable while one write is open', () => {
      const { store } = setup();
      favourites.favourite.and.returnValue(NEVER);

      store.toggle(99).subscribe();
      store.setFavourited(12, true);

      expect(store.isFavourited(12)).toBeTrue();
    });
  });

  it('announces a new session generation on sign-out', () => {
    const { store, mockStore } = setup();
    const seen: number[] = [];
    store.session$.subscribe((generation) => seen.push(generation));

    mockStore.overrideSelector(selectUser, null);
    mockStore.refreshState();

    expect(seen.length).toBe(2);
    expect(seen[1]).not.toBe(seen[0]);
  });

  it('emits per-event state through isFavourited$', () => {
    const { store } = setup();
    store.ensureLoaded();

    const seen: boolean[] = [];
    store.isFavourited$(12).subscribe((favourited) => seen.push(favourited));

    store.toggle(12).subscribe();

    expect(seen).toEqual([true, false]);
  });
});
