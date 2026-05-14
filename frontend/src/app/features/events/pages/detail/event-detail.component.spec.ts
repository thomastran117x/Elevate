import { ActivatedRoute, convertToParamMap, ParamMap, Router } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { EventDetailComponent } from './event-detail.component';
import { EventsService } from '../../services/events.service';
import { EventApiResponse } from '../../models/event.types';

class ActivatedRouteStub {
  private readonly paramSubject = new BehaviorSubject<ParamMap>(convertToParamMap({ eventId: '42' }));
  private readonly querySubject = new BehaviorSubject<ParamMap>(convertToParamMap({ search: 'hack' }));

  readonly paramMap = this.paramSubject.asObservable();
  readonly queryParamMap = this.querySubject.asObservable();
  snapshot = {
    queryParams: { search: 'hack' } as Record<string, string>,
  };

  setParamMap(params: Record<string, string>) {
    this.paramSubject.next(convertToParamMap(params));
  }

  setQueryParamMap(params: Record<string, string>) {
    this.snapshot.queryParams = params;
    this.querySubject.next(convertToParamMap(params));
  }
}

describe('EventDetailComponent', () => {
  let fixture: ComponentFixture<EventDetailComponent>;
  let component: EventDetailComponent;
  let route: ActivatedRouteStub;
  let eventsService: jasmine.SpyObj<EventsService>;
  let router: jasmine.SpyObj<Router>;

  const response: EventApiResponse = {
    success: true,
    message: 'ok',
    data: {
      id: 42,
      name: 'Hack Night',
      description: 'Build things together',
      location: 'Student Center',
      imageUrls: ['https://example.com/poster.png'],
      isPrivate: false,
      maxParticipants: 120,
      registerCost: 0,
      startTime: '2026-05-20T18:00:00Z',
      endTime: '2026-05-20T21:00:00Z',
      clubId: 7,
      createdAt: '2026-05-01T12:00:00Z',
      status: 'Upcoming',
      category: 'Workshop',
      venueName: 'Main Hall',
      city: 'Ottawa',
      latitude: 45.4215,
      longitude: -75.6972,
      tags: ['tech', 'community'],
      registrationCount: 34,
      distanceKm: undefined,
      club: {
        id: 7,
        name: 'uOttaHack',
        description: 'Hackathons and builder meetups',
        clubType: 'Academic',
        clubImage: 'https://example.com/club.png',
        memberCount: 240,
        eventCount: 18,
        availableEventCount: 3,
        isPrivate: false,
        email: 'hello@uottahack.ca',
        phone: '555-0101',
        rating: 4.8,
        websiteUrl: 'https://uottahack.ca',
        location: 'Ottawa',
      },
    },
    error: null,
    meta: null,
  };

  beforeEach(async () => {
    route = new ActivatedRouteStub();
    eventsService = jasmine.createSpyObj<EventsService>('EventsService', ['getEvent']);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    eventsService.getEvent.and.returnValue(of(response));

    await TestBed.configureTestingModule({
      imports: [EventDetailComponent],
      providers: [
        { provide: ActivatedRoute, useValue: route },
        { provide: EventsService, useValue: eventsService },
        { provide: Router, useValue: router },
      ],
    }).compileComponents();
  });

  function createComponent() {
    fixture = TestBed.createComponent(EventDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads the event from the route id', () => {
    createComponent();

    expect(eventsService.getEvent).toHaveBeenCalledWith(42);
    expect(component.event?.id).toBe(42);
    expect(component.event?.club?.name).toBe('uOttaHack');
    expect(component.loading).toBeFalse();
    expect(component.error).toBe('');
  });

  it('navigates back to the search route with the preserved query params', () => {
    createComponent();

    component.goBack();

    expect(router.navigate).toHaveBeenCalledWith(['/events'], {
      queryParams: { search: 'hack' },
    });
  });

  it('shows an error for invalid route ids', () => {
    route.setParamMap({ eventId: 'abc' });

    createComponent();

    expect(eventsService.getEvent).not.toHaveBeenCalled();
    expect(component.error).toBe('Invalid event ID.');
    expect(component.loading).toBeFalse();
  });

  it('shows an error when the request fails', () => {
    eventsService.getEvent.and.returnValue(
      throwError(() => ({
        error: { message: 'Not found.' },
      })),
    );

    createComponent();

    expect(component.event).toBeNull();
    expect(component.error).toBe('Not found.');
    expect(component.loading).toBeFalse();
  });
});
