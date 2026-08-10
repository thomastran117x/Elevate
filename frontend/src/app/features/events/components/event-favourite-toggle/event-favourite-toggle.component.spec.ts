import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { EventFavouritesStore } from '../../services/event-favourites-store.service';
import { EventFavouriteToggleComponent } from './event-favourite-toggle.component';

describe('EventFavouriteToggleComponent', () => {
  let fixture: ComponentFixture<EventFavouriteToggleComponent>;
  let component: EventFavouriteToggleComponent;
  let favouritesStore: jasmine.SpyObj<EventFavouritesStore> & { isSignedIn: boolean };
  let router: jasmine.SpyObj<Router>;
  let authReturnUrl: jasmine.SpyObj<AuthReturnUrlService>;
  let favourited$: BehaviorSubject<boolean>;

  async function setup(signedIn = true): Promise<void> {
    favourited$ = new BehaviorSubject(false);

    favouritesStore = jasmine.createSpyObj<EventFavouritesStore>(
      'EventFavouritesStore',
      ['ensureLoaded', 'toggle', 'isFavourited$'],
      { isSignedIn: signedIn },
    ) as jasmine.SpyObj<EventFavouritesStore> & { isSignedIn: boolean };
    favouritesStore.isFavourited$.and.returnValue(favourited$.asObservable());
    favouritesStore.toggle.and.returnValue(of(true));

    router = jasmine.createSpyObj<Router>('Router', ['navigate'], { url: '/events' });
    router.navigate.and.resolveTo(true);
    authReturnUrl = jasmine.createSpyObj<AuthReturnUrlService>('AuthReturnUrlService', ['set']);

    await TestBed.configureTestingModule({
      imports: [EventFavouriteToggleComponent],
      providers: [
        { provide: EventFavouritesStore, useValue: favouritesStore },
        { provide: Router, useValue: router },
        { provide: AuthReturnUrlService, useValue: authReturnUrl },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EventFavouriteToggleComponent);
    component = fixture.componentInstance;
    component.eventId = 42;
    fixture.detectChanges();
  }

  it('loads the id set and mirrors the shared star state', async () => {
    await setup();

    expect(favouritesStore.ensureLoaded).toHaveBeenCalled();
    expect(component.favourited).toBeFalse();

    favourited$.next(true);
    expect(component.favourited).toBeTrue();
  });

  it('writes through the shared store', async () => {
    await setup();

    component.toggle();

    expect(favouritesStore.toggle).toHaveBeenCalledWith(42);
    expect(component.pending).toBeFalse();
  });

  it('emits the failure so the page can show a message', async () => {
    await setup();
    const error = new Error('500');
    favouritesStore.toggle.and.returnValue(throwError(() => error));

    const failed = jasmine.createSpy('failed');
    component.failed.subscribe(failed);

    component.toggle();

    expect(failed).toHaveBeenCalledWith(error);
    expect(component.pending).toBeFalse();
  });

  it('sends signed-out visitors to login with a return url instead of erroring', async () => {
    await setup(false);

    component.toggle();

    expect(favouritesStore.toggle).not.toHaveBeenCalled();
    expect(authReturnUrl.set).toHaveBeenCalledWith('/events');
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/events' },
    });
  });

  it('re-subscribes when the row is recycled onto another event', async () => {
    await setup();
    const nextEvent$ = new BehaviorSubject(true);
    favouritesStore.isFavourited$.and.returnValue(nextEvent$.asObservable());

    component.eventId = 77;
    component.ngOnChanges();

    expect(favouritesStore.isFavourited$).toHaveBeenCalledWith(77);
    expect(component.favourited).toBeTrue();

    // The previous event's stream must no longer drive this instance.
    favourited$.next(false);
    expect(component.favourited).toBeTrue();
  });
});
