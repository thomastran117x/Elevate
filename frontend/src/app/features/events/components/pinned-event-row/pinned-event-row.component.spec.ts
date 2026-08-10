import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { makeEventItem } from '@testing';

import { PinnedEvent } from '../../models/event-favourite.types';
import { EventFavouritesStore } from '../../services/event-favourites-store.service';
import { PinnedEventRowComponent } from './pinned-event-row.component';

describe('PinnedEventRowComponent', () => {
  let fixture: ComponentFixture<PinnedEventRowComponent>;
  let component: PinnedEventRowComponent;

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

  beforeEach(async () => {
    const favouritesStore = jasmine.createSpyObj<EventFavouritesStore>(
      'EventFavouritesStore',
      ['ensureLoaded', 'toggle', 'isFavourited$'],
      { isSignedIn: true },
    );
    favouritesStore.isFavourited$.and.returnValue(of(true));

    await TestBed.configureTestingModule({
      imports: [PinnedEventRowComponent],
      providers: [provideRouter([]), { provide: EventFavouritesStore, useValue: favouritesStore }],
    }).compileComponents();

    fixture = TestBed.createComponent(PinnedEventRowComponent);
    component = fixture.componentInstance;
  });

  function render(item: PinnedEvent, favourited = true): string {
    component.item = item;
    component.favourited = favourited;
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('marks a registered row as Going', () => {
    const text = render(pinnedRow({ isRegistered: true }));

    expect(component.isGoing).toBeTrue();
    expect(text).toContain('Going');
  });

  it('omits the Going badge for a saved-only row', () => {
    const text = render(pinnedRow());

    expect(component.isGoing).toBeFalse();
    expect(text).not.toContain('Going');
  });

  it('explains why an unstarred row is still on screen', () => {
    const text = render(pinnedRow({ isFavourited: true }), false);

    expect(component.isPendingRemoval).toBeTrue();
    expect(text).toContain('Removed from saved');
  });

  it('shows no removal hint while the star is still filled', () => {
    const text = render(pinnedRow({ isFavourited: true }), true);

    expect(component.isPendingRemoval).toBeFalse();
    expect(text).not.toContain('Removed from saved');
  });

  it('shows no removal hint for a row that was never starred', () => {
    render(pinnedRow({ isRegistered: true, isFavourited: false }), false);

    expect(component.isPendingRemoval).toBeFalse();
  });

  it('withholds details but keeps the row when access was revoked', () => {
    const text = render(
      pinnedRow({ accessRevoked: true, event: makeEventItem({ name: 'Secret Gala' }) }),
    );

    expect(text).toContain('Event no longer available to you');
    expect(text).not.toContain('Secret Gala');
  });

  it('flags a cancelled event', () => {
    const text = render(pinnedRow({ event: makeEventItem({ lifecycleState: 'Cancelled' }) }));

    expect(component.isCancelled).toBeTrue();
    expect(text).toContain('Event cancelled');
  });
});
