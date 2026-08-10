import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';

import { makeEventItem } from '@testing';

import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { PinnedEvent } from '../../models/event-favourite.types';
import { EventFavouritesService } from '../../services/event-favourites.service';
import { EventFavouritesStore } from '../../services/event-favourites-store.service';
import { MyPinnedComponent } from './my-pinned.component';

describe('MyPinnedComponent', () => {
  let fixture: ComponentFixture<MyPinnedComponent>;
  let component: MyPinnedComponent;
  let favourites: jasmine.SpyObj<EventFavouritesService>;
  let favouritesStore: jasmine.SpyObj<EventFavouritesStore> & { isSignedIn: boolean };
  let navigate: jasmine.Spy;
  let authReturnUrl: jasmine.SpyObj<AuthReturnUrlService>;
  let ids$: BehaviorSubject<ReadonlySet<number>>;
  let session$: BehaviorSubject<number>;
  let signedIn: boolean;

  function pinnedRow(overrides: Partial<PinnedEvent> = {}): PinnedEvent {
    return {
      isRegistered: false,
      isFavourited: true,
      favouritedAtUtc: '2026-08-01T00:00:00Z',
      registeredAtUtc: null,
      accessRevoked: false,
      event: makeEventItem(),
      ...overrides,
    };
  }

  async function setup(rows: PinnedEvent[] = []): Promise<void> {
    ids$ = new BehaviorSubject<ReadonlySet<number>>(
      new Set(rows.filter((row) => row.isFavourited).map((row) => row.event.id)),
    );
    session$ = new BehaviorSubject(0);
    signedIn = true;

    favourites = jasmine.createSpyObj<EventFavouritesService>('EventFavouritesService', [
      'getMyPinned',
    ]);
    favourites.getMyPinned.and.returnValue(of(rows));

    favouritesStore = jasmine.createSpyObj<EventFavouritesStore>(
      'EventFavouritesStore',
      ['setFavourited', 'toggle', 'ensureLoaded', 'isFavourited$', 'isCurrentSession'],
      {
        ids$: ids$.asObservable(),
        sessionGeneration: 0,
        session$: session$.asObservable(),
      },
    ) as jasmine.SpyObj<EventFavouritesStore> & { isSignedIn: boolean };
    // A live getter, so a spec can end the session mid-test the way a logout does.
    Object.defineProperty(favouritesStore, 'isSignedIn', {
      get: () => signedIn,
      configurable: true,
    });
    favouritesStore.isFavourited$.and.callFake((eventId: number) => of(ids$.value.has(eventId)));
    favouritesStore.toggle.and.returnValue(of(false));
    favouritesStore.isCurrentSession.and.returnValue(true);

    authReturnUrl = jasmine.createSpyObj<AuthReturnUrlService>('AuthReturnUrlService', ['set']);

    await TestBed.configureTestingModule({
      imports: [MyPinnedComponent],
      providers: [
        provideRouter([]),
        { provide: EventFavouritesService, useValue: favourites },
        { provide: EventFavouritesStore, useValue: favouritesStore },
        { provide: AuthReturnUrlService, useValue: authReturnUrl },
      ],
    }).compileComponents();

    const router = TestBed.inject(Router);
    navigate = spyOn(router, 'navigate').and.resolveTo(true);
    spyOnProperty(router, 'url', 'get').and.returnValue('/events/me/pinned');

    fixture = TestBed.createComponent(MyPinnedComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('splits the snapshot into Going and Saved', async () => {
    await setup([
      pinnedRow({ isRegistered: true, isFavourited: false, event: makeEventItem({ id: 9 }) }),
      pinnedRow({ event: makeEventItem({ id: 4 }) }),
    ]);

    expect(component.loading).toBeFalse();
    expect(component.going.map((row) => row.event.id)).toEqual([9]);
    expect(component.saved.map((row) => row.event.id)).toEqual([4]);
  });

  it('seeds the shared star state from the loaded rows', async () => {
    await setup([
      pinnedRow({ isRegistered: true, isFavourited: false, event: makeEventItem({ id: 9 }) }),
      pinnedRow({ event: makeEventItem({ id: 4 }) }),
    ]);

    expect(favouritesStore.setFavourited).toHaveBeenCalledWith(9, false);
    expect(favouritesStore.setFavourited).toHaveBeenCalledWith(4, true);
  });

  it('keeps an unstarred row on screen and does not reload', async () => {
    await setup([pinnedRow({ event: makeEventItem({ id: 4 }) })]);
    expect(component.isFavourited(component.items[0])).toBeTrue();

    // Simulate the toggle component writing through the shared store.
    ids$.next(new Set<number>());
    fixture.detectChanges();

    // The row survives so the star is its own undo — this is the whole backtrack behaviour.
    expect(component.items.length).toBe(1);
    expect(component.saved.length).toBe(1);
    expect(component.isFavourited(component.items[0])).toBeFalse();
    expect(favourites.getMyPinned).toHaveBeenCalledTimes(1);
  });

  it('restores the star when the user changes their mind', async () => {
    await setup([pinnedRow({ event: makeEventItem({ id: 4 }) })]);

    ids$.next(new Set<number>());
    ids$.next(new Set([4]));
    fixture.detectChanges();

    expect(component.isFavourited(component.items[0])).toBeTrue();
    expect(favourites.getMyPinned).toHaveBeenCalledTimes(1);
  });

  it('keeps a Going row regardless of its star', async () => {
    await setup([
      pinnedRow({ isRegistered: true, isFavourited: true, event: makeEventItem({ id: 9 }) }),
    ]);

    ids$.next(new Set<number>());
    fixture.detectChanges();

    // It is on the list because of the registration, not the star.
    expect(component.going.length).toBe(1);
  });

  it('discards a response that lands after the session changed', async () => {
    await setup();

    const pinned$ = new Subject<PinnedEvent[]>();
    favourites.getMyPinned.and.returnValue(pinned$.asObservable());
    // Signing out clears user state without navigating, so this page is still mounted.
    let currentSession = true;
    favouritesStore.isCurrentSession.and.callFake(() => currentSession);

    fixture = TestBed.createComponent(MyPinnedComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    currentSession = false;
    pinned$.next([pinnedRow({ event: makeEventItem({ id: 4 }) })]);

    expect(component.items).toEqual([]);
    expect(favouritesStore.setFavourited).not.toHaveBeenCalled();
  });

  it('leaves the next account rows alone when the previous response arrives late', async () => {
    await setup();

    const stale$ = new Subject<PinnedEvent[]>();
    favourites.getMyPinned.and.returnValue(stale$.asObservable());
    let generation = 0;
    favouritesStore.isCurrentSession.and.callFake((captured: number) => captured === generation);
    Object.defineProperty(favouritesStore, 'sessionGeneration', {
      get: () => generation,
      configurable: true,
    });

    fixture = TestBed.createComponent(MyPinnedComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    // Account switch: the new account's reload resolves first.
    generation = 1;
    favourites.getMyPinned.and.returnValue(of([pinnedRow({ event: makeEventItem({ id: 9 }) })]));
    session$.next(1);
    expect(component.items.map((row) => row.event.id)).toEqual([9]);

    // The previous account's request finally lands. It must not delete the rows on screen.
    stale$.next([pinnedRow({ event: makeEventItem({ id: 4 }) })]);

    expect(component.items.map((row) => row.event.id)).toEqual([9]);
    expect(component.loading).toBeFalse();
  });

  it('ignores a failure from a request the session already replaced', async () => {
    await setup();

    const stale$ = new Subject<PinnedEvent[]>();
    favourites.getMyPinned.and.returnValue(stale$.asObservable());
    let generation = 0;
    favouritesStore.isCurrentSession.and.callFake((captured: number) => captured === generation);
    Object.defineProperty(favouritesStore, 'sessionGeneration', {
      get: () => generation,
      configurable: true,
    });

    fixture = TestBed.createComponent(MyPinnedComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    generation = 1;
    favourites.getMyPinned.and.returnValue(of([pinnedRow({ event: makeEventItem({ id: 9 }) })]));
    session$.next(1);

    // A 401 belonging to the signed-out session must not send the new one to the login prompt.
    stale$.error({ status: 401 });

    expect(component.requiresLogin).toBeFalse();
    expect(component.error).toBe('');
    expect(component.items.map((row) => row.event.id)).toEqual([9]);
  });

  it('drops already-loaded rows when the session ends', async () => {
    await setup([pinnedRow({ event: makeEventItem({ id: 4 }) })]);
    expect(component.items.length).toBe(1);

    // Logging out does not navigate, so without this the previous user's pinned events keep
    // rendering in the signed-out session.
    signedIn = false;
    session$.next(1);

    expect(component.items).toEqual([]);
    expect(component.requiresLogin).toBeTrue();
    expect(component.loading).toBeFalse();
  });

  it('reloads for the next user when the account switches', async () => {
    await setup([pinnedRow({ event: makeEventItem({ id: 4 }) })]);
    favourites.getMyPinned.calls.reset();
    favourites.getMyPinned.and.returnValue(of([pinnedRow({ event: makeEventItem({ id: 9 }) })]));

    session$.next(1);

    expect(favourites.getMyPinned).toHaveBeenCalledTimes(1);
    expect(component.items.map((row) => row.event.id)).toEqual([9]);
    expect(component.requiresLogin).toBeFalse();
  });

  it('surfaces a message when a toggle fails', async () => {
    await setup([pinnedRow()]);

    component.onFavouriteFailed({ status: 500 });

    expect(component.error).toBe('We could not update this event.');
  });

  it('offers sign-in on a 401 instead of an error banner', async () => {
    await setup();
    favourites.getMyPinned.and.returnValue(throwError(() => ({ status: 401 })));

    fixture = TestBed.createComponent(MyPinnedComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.requiresLogin).toBeTrue();
    expect(component.error).toBe('');
  });

  it('shows an error banner for non-401 load failures', async () => {
    await setup();
    favourites.getMyPinned.and.returnValue(throwError(() => ({ status: 500 })));

    fixture = TestBed.createComponent(MyPinnedComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.requiresLogin).toBeFalse();
    expect(component.error).toBe('We could not load your pinned events.');
  });

  it('sends the visitor to login with a return url', async () => {
    await setup();

    component.goToLogin();

    expect(authReturnUrl.set).toHaveBeenCalledWith('/events/me/pinned');
    expect(navigate).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/events/me/pinned' },
    });
  });
});
