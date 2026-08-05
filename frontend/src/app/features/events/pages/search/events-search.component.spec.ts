import { ActivatedRoute, convertToParamMap, ParamMap, Router } from '@angular/router';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { makeEventItem } from '@testing';

import { EventsSearchComponent } from './events-search.component';
import { EventsService } from '../../services/events.service';
import { FeatureFlagsService } from '../../../../core/features/feature-flags.service';
import { EventsApiResponse } from '../../models/event.types';
import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
} from '../../../../core/api/models/api-client-error.model';

class ActivatedRouteStub {
  private readonly subject = new BehaviorSubject<ParamMap>(convertToParamMap({}));

  readonly queryParamMap = this.subject.asObservable();
  snapshot = { queryParams: {} as Record<string, string> };

  setQueryParams(params: Record<string, string>) {
    this.snapshot.queryParams = params;
    this.subject.next(convertToParamMap(params));
  }
}

describe('EventsSearchComponent', () => {
  let fixture: ComponentFixture<EventsSearchComponent>;
  let component: EventsSearchComponent;
  let route: ActivatedRouteStub;
  let eventsService: jasmine.SpyObj<EventsService>;
  let router: jasmine.SpyObj<Router>;

  const response: EventsApiResponse = {
    success: true,
    message: 'ok',
    data: {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
    },
    error: null,
    meta: { source: 'elasticsearch' },
  };

  let waitlistFeatureEnabled = true;

  beforeEach(async () => {
    route = new ActivatedRouteStub();
    eventsService = jasmine.createSpyObj<EventsService>('EventsService', ['getEvents']);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    eventsService.getEvents.and.returnValue(of(response));

    await TestBed.configureTestingModule({
      imports: [EventsSearchComponent],
      providers: [
        { provide: ActivatedRoute, useValue: route },
        { provide: EventsService, useValue: eventsService },
        { provide: Router, useValue: router },
        {
          provide: FeatureFlagsService,
          useValue: { isEnabled: () => waitlistFeatureEnabled },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => (waitlistFeatureEnabled = true));

  it('exposes the waitlist feature flag so cards do not advertise a gated feature', () => {
    createComponent();
    expect(component.waitlistFeatureEnabled).toBeTrue();
  });

  it('reports the waitlist feature as unavailable when the flag is off', () => {
    waitlistFeatureEnabled = false;
    createComponent();
    expect(component.waitlistFeatureEnabled).toBeFalse();
  });

  function createComponent() {
    fixture = TestBed.createComponent(EventsSearchComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('hydrates its state from valid URL params including tags and nearby filters', () => {
    route.setQueryParams({
      search: 'hackathon',
      city: 'Ottawa',
      category: 'Workshop',
      status: 'Upcoming',
      sort: 'Distance',
      tags: 'free,student',
      lat: '45.4215',
      lng: '-75.6972',
      radiusKm: '25',
      page: '3',
    });

    createComponent();

    expect(component.searchQuery).toBe('hackathon');
    expect(component.cityQuery).toBe('Ottawa');
    expect(component.selectedCategory).toBe('Workshop');
    expect(component.selectedStatus).toBe('Upcoming');
    expect(component.selectedSort).toBe('Distance');
    expect(component.tags).toEqual(['free', 'student']);
    expect(component.latitude).toBe(45.4215);
    expect(component.longitude).toBe(-75.6972);
    expect(component.radiusKm).toBe(25);
    expect(component.currentPage).toBe(3);
    expect(eventsService.getEvents).toHaveBeenCalledWith(
      jasmine.objectContaining({
        search: 'hackathon',
        city: 'Ottawa',
        category: 'Workshop',
        status: 'Upcoming',
        sortBy: 'Distance',
        tags: 'free,student',
        lat: 45.4215,
        lng: -75.6972,
        radiusKm: 25,
        page: 3,
      }),
    );
  });

  it('debounces text changes before syncing the URL', fakeAsync(() => {
    createComponent();
    router.navigate.calls.reset();

    component.searchQuery = 'new query';
    component.onTextChange();

    tick(399);
    expect(router.navigate).not.toHaveBeenCalled();

    tick(1);
    expect(router.navigate).toHaveBeenCalledWith([], {
      relativeTo: route as unknown as ActivatedRoute,
      queryParams: { search: 'new query' },
      replaceUrl: true,
    });
  }));

  it('resets the page when tags change', () => {
    route.setQueryParams({ page: '4' });
    createComponent();
    router.navigate.calls.reset();

    component.tagInput = 'free,student';
    component.addTagFromInput();

    expect(component.currentPage).toBe(1);
    expect(component.tags).toEqual(['free', 'student']);
    expect(router.navigate).toHaveBeenCalledWith([], {
      relativeTo: route as unknown as ActivatedRoute,
      queryParams: { tags: 'free,student' },
      replaceUrl: true,
    });
  });

  it('canonicalizes invalid nearby state from the URL', () => {
    route.setQueryParams({
      sort: 'Distance',
      radiusKm: '25',
      page: '0',
    });

    createComponent();

    expect(component.selectedSort).toBe('Relevance');
    expect(component.radiusKm).toBeNull();
    expect(component.currentPage).toBe(1);
    expect(router.navigate).toHaveBeenCalledWith([], {
      relativeTo: route as unknown as ActivatedRoute,
      queryParams: {},
      replaceUrl: true,
    });
    expect(eventsService.getEvents).not.toHaveBeenCalled();
  });

  it('surfaces 4xx request failures from the adapter', () => {
    eventsService.getEvents.and.returnValue(
      throwError(() => new ApiClientClientError('Search failed.', 422, 'VALIDATION_ERROR')),
    );

    createComponent();

    expect(component.error).toBe('Search failed.');
    expect(component.events).toEqual([]);
    expect(component.totalCount).toBe(0);
    expect(component.totalPages).toBe(0);
    expect(component.loading).toBeFalse();
  });

  it('shows the generic adapter message for 5xx failures', () => {
    eventsService.getEvents.and.returnValue(
      throwError(() => new ApiClientServerError(GENERIC_API_ERROR_MESSAGE, 500)),
    );

    createComponent();

    expect(component.error).toBe(GENERIC_API_ERROR_MESSAGE);
    expect(component.events).toEqual([]);
    expect(component.totalCount).toBe(0);
    expect(component.totalPages).toBe(0);
    expect(component.loading).toBeFalse();
  });

  /** The query params the component last asked the router to write. */
  function lastQueryParams(): Record<string, string> {
    const calls = router.navigate.calls.all();
    return calls[calls.length - 1].args[1]?.queryParams as Record<string, string>;
  }

  describe('results', () => {
    it('stores the page and the index that served it', () => {
      eventsService.getEvents.and.returnValue(
        of({
          ...response,
          data: { items: [makeEventItem()], totalCount: 1, page: 1, pageSize: 20, totalPages: 1 },
        }),
      );

      createComponent();

      expect(component.events.length).toBe(1);
      expect(component.totalCount).toBe(1);
      expect(component.sourceLabel).toBe('Search index');
    });

    it('labels a database fallback distinctly', () => {
      eventsService.getEvents.and.returnValue(of({ ...response, meta: { source: 'database' } }));

      createComponent();

      expect(component.sourceLabel).toBe('Fallback results');
    });

    it('passes any other source label through unchanged', () => {
      eventsService.getEvents.and.returnValue(of({ ...response, meta: { source: 'cache' } }));

      createComponent();

      expect(component.sourceLabel).toBe('cache');
    });

    it('reports the envelope message when a successful response carries no data', () => {
      eventsService.getEvents.and.returnValue(
        of({ ...response, data: null, message: 'Search is temporarily unavailable.' }),
      );

      createComponent();

      expect(component.error).toBe('Search is temporarily unavailable.');
      expect(component.events).toEqual([]);
      expect(component.loading).toBeFalse();
    });

    it('falls back to a generic message when the empty envelope has none', () => {
      eventsService.getEvents.and.returnValue(of({ ...response, data: null, message: '' }));

      createComponent();

      expect(component.error).toBe('Failed to load events. Please try again.');
    });
  });

  describe('tag entry', () => {
    beforeEach(() => createComponent());

    it('commits the typed tags on Enter', () => {
      const event = new KeyboardEvent('keydown', { key: 'Enter' });
      spyOn(event, 'preventDefault');
      component.tagInput = 'Free, Student';

      component.onTagInputKeydown(event);

      expect(event.preventDefault).toHaveBeenCalled();
      expect(component.tags).toEqual(['free', 'student']);
      expect(component.tagInput).toBe('');
    });

    it('commits on a comma as well', () => {
      component.tagInput = 'free';

      component.onTagInputKeydown(new KeyboardEvent('keydown', { key: ',' }));

      expect(component.tags).toEqual(['free']);
    });

    it('backspaces the last tag only when the input is empty', () => {
      component.tagInput = 'free,student';
      component.addTagFromInput();

      component.tagInput = 'part';
      component.onTagInputKeydown(new KeyboardEvent('keydown', { key: 'Backspace' }));
      expect(component.tags).toEqual(['free', 'student']);

      component.tagInput = '';
      component.onTagInputKeydown(new KeyboardEvent('keydown', { key: 'Backspace' }));
      expect(component.tags).toEqual(['free']);
    });

    it('ignores Backspace when there are no tags', () => {
      component.tagInput = '';

      expect(() =>
        component.onTagInputKeydown(new KeyboardEvent('keydown', { key: 'Backspace' })),
      ).not.toThrow();
      expect(component.tags).toEqual([]);
    });

    it('leaves other keys alone', () => {
      component.tagInput = 'fre';

      component.onTagInputKeydown(new KeyboardEvent('keydown', { key: 'e' }));

      expect(component.tags).toEqual([]);
      expect(component.tagInput).toBe('fre');
    });

    it('discards blank input without touching the URL', () => {
      router.navigate.calls.reset();
      component.tagInput = '  ,  ';

      component.addTagFromInput();

      expect(component.tags).toEqual([]);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('does not re-sync when every typed tag is already applied', () => {
      component.tagInput = 'free';
      component.addTagFromInput();
      router.navigate.calls.reset();

      component.tagInput = 'free';
      component.addTagFromInput();

      expect(component.tags).toEqual(['free']);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('caps the tag list', () => {
      component.tagInput = 'a,b,c,d,e,f,g,h,i,j,k,l';

      component.addTagFromInput();

      expect(component.tags.length).toBeLessThanOrEqual(8);
    });

    it('adds a suggested tag once', () => {
      component.addSuggestedTag('free');
      expect(component.tags).toEqual(['free']);

      router.navigate.calls.reset();
      component.addSuggestedTag('free');
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('removes a tag, ignoring one that is not applied', () => {
      component.tagInput = 'free,student';
      component.addTagFromInput();

      component.removeTag('student');
      expect(component.tags).toEqual(['free']);

      router.navigate.calls.reset();
      component.removeTag('missing');
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  describe('location', () => {
    let getCurrentPosition: jasmine.Spy;

    beforeEach(() => {
      getCurrentPosition = jasmine.createSpy('getCurrentPosition');
      Object.defineProperty(navigator, 'geolocation', {
        value: { getCurrentPosition },
        configurable: true,
      });
      createComponent();
    });

    it('adopts the browser position, defaults the radius and sorts by distance', () => {
      component.useCurrentLocation();
      const [onSuccess] = getCurrentPosition.calls.mostRecent().args;

      onSuccess({ coords: { latitude: 45.42151234, longitude: -75.69721234 } });

      expect(component.latitude).toBe(45.421512);
      expect(component.longitude).toBe(-75.697212);
      expect(component.radiusKm).toBe(25);
      expect(component.selectedSort).toBe('Distance');
      expect(component.locatingUser).toBeFalse();
    });

    it('keeps a radius the user already chose', () => {
      component.radiusKm = 5;

      component.useCurrentLocation();
      getCurrentPosition.calls.mostRecent().args[0]({
        coords: { latitude: 45, longitude: -75 },
      });

      expect(component.radiusKm).toBe(5);
    });

    it('reports a denied lookup', () => {
      component.useCurrentLocation();

      getCurrentPosition.calls.mostRecent().args[1]({ message: 'User denied Geolocation' });

      expect(component.geolocationError).toBe('User denied Geolocation');
      expect(component.locatingUser).toBeFalse();
    });

    it('falls back to a generic message when the error carries none', () => {
      component.useCurrentLocation();

      getCurrentPosition.calls.mostRecent().args[1]({ message: '' });

      expect(component.geolocationError).toBe('Unable to retrieve your location.');
    });

    it('reports a browser without geolocation support', () => {
      Object.defineProperty(navigator, 'geolocation', { value: undefined, configurable: true });

      component.useCurrentLocation();

      expect(component.geolocationError).toBe('This browser does not support location access.');
      expect(component.locatingUser).toBeFalse();
    });

    it('formats the coordinate label, and blanks it without coordinates', () => {
      expect(component.coordinateLabel).toBe('');
      expect(component.hasCoordinates).toBeFalse();

      component.latitude = 45.4215;
      component.longitude = -75.6972;

      expect(component.hasCoordinates).toBeTrue();
      expect(component.coordinateLabel).toBe('45.42, -75.70');
    });

    it('clears the location and drops a distance sort', () => {
      component.latitude = 45;
      component.longitude = -75;
      component.radiusKm = 10;
      component.selectedSort = 'Distance';

      component.clearLocation();

      expect(component.hasCoordinates).toBeFalse();
      expect(component.radiusKm).toBeNull();
      expect(component.selectedSort).toBe('Relevance');
      expect(lastQueryParams()).toEqual({});
    });

    it('does not re-sync when there was no location to clear', () => {
      router.navigate.calls.reset();

      component.clearLocation();

      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('offers the chosen radius alongside the presets', () => {
      const presets = component.availableRadiusOptions;
      component.radiusKm = 7;

      const withCustom = component.availableRadiusOptions;

      expect(withCustom).toContain(7);
      expect(withCustom.length).toBe(presets.length + 1);
      expect([...withCustom].sort((a, b) => a - b)).toEqual(withCustom);
    });

    it('drops a distance sort on filter change when there are no coordinates', () => {
      component.selectedSort = 'Distance';

      component.onFilterChange();

      expect(component.selectedSort).toBe('Relevance');
    });

    it('keeps a distance sort while coordinates are set', () => {
      component.latitude = 45;
      component.longitude = -75;
      component.selectedSort = 'Distance';

      component.onFilterChange();

      expect(component.selectedSort).toBe('Distance');
    });
  });

  describe('filter chips', () => {
    beforeEach(() => createComponent());

    it('lists a chip per active filter', () => {
      expect(component.hasActiveFilters).toBeFalse();

      component.searchQuery = 'hackathon';
      component.cityQuery = 'Ottawa';
      component.selectedCategory = 'Workshop';
      component.selectedStatus = 'Upcoming';
      component.selectedSort = 'Popularity';
      component.tags = ['free'];
      component.latitude = 45;
      component.longitude = -75;
      component.radiusKm = 25;

      expect(component.activeFilters.map((chip) => chip.kind)).toEqual([
        'search',
        'city',
        'category',
        'status',
        'sort',
        'tag',
        'location',
        'radius',
      ]);
      expect(component.activeFilters[4].label).toBe('Sort: Most popular');
    });

    const chipCases = [
      ['search', () => (component.searchQuery = 'x'), () => component.searchQuery, ''],
      ['city', () => (component.cityQuery = 'x'), () => component.cityQuery, ''],
      [
        'category',
        () => (component.selectedCategory = 'Workshop'),
        () => component.selectedCategory,
        null,
      ],
      [
        'status',
        () => (component.selectedStatus = 'Upcoming'),
        () => component.selectedStatus,
        null,
      ],
      ['sort', () => (component.selectedSort = 'Date'), () => component.selectedSort, 'Relevance'],
      ['radius', () => (component.radiusKm = 25), () => component.radiusKm, null],
    ] as const;

    for (const [kind, arrange, read, cleared] of chipCases) {
      it(`clears the ${kind} chip`, () => {
        arrange();

        component.clearChip({ kind, label: kind } as never);

        expect(read()).toBe(cleared as never);
        expect(component.currentPage).toBe(1);
      });
    }

    it('clears a single tag chip by value', () => {
      component.tags = ['free', 'student'];

      component.clearChip({ kind: 'tag', value: 'free', label: '#free' });

      expect(component.tags).toEqual(['student']);
    });

    it('leaves the tags alone when the tag chip has no value', () => {
      component.tags = ['free'];

      component.clearChip({ kind: 'tag', label: '#free' });

      expect(component.tags).toEqual(['free']);
    });

    it('clears coordinates, radius and a distance sort from the location chip', () => {
      component.latitude = 45;
      component.longitude = -75;
      component.radiusKm = 25;
      component.selectedSort = 'Distance';

      component.clearChip({ kind: 'location', label: 'Nearby' });

      expect(component.hasCoordinates).toBeFalse();
      expect(component.radiusKm).toBeNull();
      expect(component.selectedSort).toBe('Relevance');
    });

    it('keeps a non-distance sort when the location chip is cleared', () => {
      component.latitude = 45;
      component.longitude = -75;
      component.selectedSort = 'Date';

      component.clearChip({ kind: 'location', label: 'Nearby' });

      expect(component.selectedSort).toBe('Date');
    });

    it('clears every filter at once', () => {
      component.searchQuery = 'hackathon';
      component.cityQuery = 'Ottawa';
      component.selectedCategory = 'Workshop';
      component.selectedStatus = 'Upcoming';
      component.selectedSort = 'Date';
      component.tags = ['free'];
      component.tagInput = 'draft';
      component.latitude = 45;
      component.longitude = -75;
      component.radiusKm = 25;
      component.geolocationError = 'stale';
      component.currentPage = 3;

      component.clearFilters();

      expect(component.hasActiveFilters).toBeFalse();
      expect(component.tagInput).toBe('');
      expect(component.geolocationError).toBe('');
      expect(component.currentPage).toBe(1);
      expect(lastQueryParams()).toEqual({});
    });
  });

  describe('navigation and pagination', () => {
    beforeEach(() => {
      createComponent();
      spyOn(window, 'scrollTo');
    });

    it('carries the current query string onto the detail page', () => {
      route.setQueryParams({ search: 'hackathon' });
      router.navigate.calls.reset();

      component.viewEvent(7);

      expect(router.navigate).toHaveBeenCalledWith(['/events', 7], {
        queryParams: { search: 'hackathon' },
      });
    });

    it('selects a category and resets to page one', () => {
      component.currentPage = 4;

      component.selectCategory('Workshop');

      expect(component.selectedCategory).toBe('Workshop');
      expect(component.currentPage).toBe(1);
      expect(lastQueryParams()).toEqual({ category: 'Workshop' });
    });

    it('ignores a page outside the range', () => {
      component.totalPages = 10;
      router.navigate.calls.reset();

      component.goToPage(0);
      component.goToPage(11);

      expect(router.navigate).not.toHaveBeenCalled();
      expect(window.scrollTo).not.toHaveBeenCalled();
    });

    it('navigates and scrolls to the top for a valid page', () => {
      component.totalPages = 10;

      component.goToPage(4);

      expect(component.currentPage).toBe(4);
      expect(lastQueryParams()).toEqual({ page: '4' });
      expect(window.scrollTo).toHaveBeenCalled();
    });

    it('lists every page when there are few enough', () => {
      component.totalPages = 5;

      expect(component.pageNumbers).toEqual([1, 2, 3, 4, 5]);
    });

    it('elides the middle of a long page list', () => {
      component.totalPages = 10;
      component.currentPage = 5;

      expect(component.pageNumbers).toEqual([1, -1, 4, 5, 6, -1, 10]);
    });

    it('omits the leading ellipsis near the start', () => {
      component.totalPages = 10;
      component.currentPage = 2;

      expect(component.pageNumbers).toEqual([1, 2, 3, -1, 10]);
    });

    it('omits the trailing ellipsis near the end', () => {
      component.totalPages = 10;
      component.currentPage = 9;

      expect(component.pageNumbers).toEqual([1, -1, 8, 9, 10]);
    });
  });

  describe('card display helpers', () => {
    beforeEach(() => createComponent());

    it('formats dates, times and costs', () => {
      expect(component.formatDate('2026-09-01T18:00:00Z')).toContain('2026');
      expect(component.formatTime('2026-09-01T18:00:00Z')).toMatch(/\d{2}:\d{2}/);
      expect(component.formatCost(0)).toBe('Free');
      expect(component.formatCost(15)).toBe('$15');
    });

    it('reports registration as a clamped percentage', () => {
      expect(
        component.registrationPercent(
          makeEventItem({ registrationCount: 10, maxParticipants: 40 }),
        ),
      ).toBe(25);
      expect(
        component.registrationPercent(
          makeEventItem({ registrationCount: 80, maxParticipants: 40 }),
        ),
      ).toBe(100);
      expect(
        component.registrationPercent(makeEventItem({ registrationCount: 5, maxParticipants: 0 })),
      ).toBe(0);
    });

    it('marks an event full only once it has a cap it has reached', () => {
      expect(
        component.isFull(makeEventItem({ registrationCount: 40, maxParticipants: 40 })),
      ).toBeTrue();
      expect(
        component.isFull(makeEventItem({ registrationCount: 39, maxParticipants: 40 })),
      ).toBeFalse();
      expect(
        component.isFull(makeEventItem({ registrationCount: 99, maxParticipants: 0 })),
      ).toBeFalse();
    });
  });

  describe('URL parsing edge cases', () => {
    it('discards junk category, status, sort and page values', () => {
      route.setQueryParams({
        category: 'Interpretive Dance',
        status: 'Vibes',
        sort: 'Chaos',
        page: 'three',
      });

      createComponent();

      expect(component.selectedCategory).toBeNull();
      expect(component.selectedStatus).toBeNull();
      expect(component.selectedSort).toBe('Relevance');
      expect(component.currentPage).toBe(1);
    });

    it('drops a latitude on its own and the radius that came with it', () => {
      route.setQueryParams({ lat: '45.4215', radiusKm: '25' });

      createComponent();

      expect(component.latitude).toBeNull();
      expect(component.longitude).toBeNull();
      expect(component.radiusKm).toBeNull();
    });

    it('rejects out-of-range coordinates', () => {
      route.setQueryParams({ lat: '120', lng: '-75.6972' });

      createComponent();

      expect(component.latitude).toBeNull();
      expect(component.longitude).toBeNull();
    });

    it('rejects a radius outside the supported band', () => {
      route.setQueryParams({ lat: '45', lng: '-75', radiusKm: '900' });

      createComponent();

      expect(component.hasCoordinates).toBeTrue();
      expect(component.radiusKm).toBeNull();
    });

    it('lowercases and de-duplicates tags from the URL', () => {
      route.setQueryParams({ tags: 'Free, FREE ,student, ' });

      createComponent();

      expect(component.tags).toEqual(['free', 'student']);
    });
  });
});
