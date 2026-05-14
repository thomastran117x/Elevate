import { ActivatedRoute, convertToParamMap, ParamMap, Router } from '@angular/router';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { EventsSearchComponent } from './events-search.component';
import { EventsService } from '../../services/events.service';
import { EventsApiResponse } from '../../models/event.types';

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
      ],
    }).compileComponents();
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

  it('shows an error and clears stale results when the request fails', () => {
    eventsService.getEvents.and.returnValue(
      throwError(() => ({
        error: { message: 'Search failed.' },
      })),
    );

    createComponent();

    expect(component.error).toBe('Search failed.');
    expect(component.events).toEqual([]);
    expect(component.totalCount).toBe(0);
    expect(component.totalPages).toBe(0);
    expect(component.loading).toBeFalse();
  });
});
