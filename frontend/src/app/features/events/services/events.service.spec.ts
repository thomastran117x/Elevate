import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EventsService } from './events.service';

describe('EventsService', () => {
  let service: EventsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [EventsService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(EventsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('serializes the supported search filters', () => {
    service
      .getEvents({
        search: '  hack night  ',
        city: '  Ottawa ',
        category: 'Workshop',
        status: 'Upcoming',
        sortBy: 'Distance',
        tags: '  free,student ',
        lat: 45.4215,
        lng: -75.6972,
        radiusKm: 25,
        page: 2,
        pageSize: 12,
      })
      .subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/events'));

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('search')).toBe('hack night');
    expect(request.request.params.get('city')).toBe('Ottawa');
    expect(request.request.params.get('category')).toBe('Workshop');
    expect(request.request.params.get('status')).toBe('Upcoming');
    expect(request.request.params.get('sortBy')).toBe('Distance');
    expect(request.request.params.get('tags')).toBe('free,student');
    expect(request.request.params.get('lat')).toBe('45.4215');
    expect(request.request.params.get('lng')).toBe('-75.6972');
    expect(request.request.params.get('radiusKm')).toBe('25');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('12');

    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('normalizes PascalCase event payloads from the backend', () => {
    let responseBody: unknown;

    service.getEvents({ page: 1, pageSize: 20 }).subscribe((response) => {
      responseBody = response.data;
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/events'));

    request.flush({
      success: true,
      message: 'ok',
      data: {
        Items: [
          {
            Id: 42,
            Name: 'Hack Night',
            Description: 'Build things together',
            Location: 'Student Center',
            ImageUrls: ['https://example.com/poster.png'],
            IsPrivate: false,
            MaxParticipants: 120,
            RegisterCost: 0,
            StartTime: '2026-05-20T18:00:00Z',
            EndTime: '2026-05-20T21:00:00Z',
            ClubId: 7,
            CreatedAt: '2026-05-01T12:00:00Z',
            Status: 'Upcoming',
            Category: 'Workshop',
            VenueName: 'Main Hall',
            City: 'Ottawa',
            Latitude: 45.4215,
            Longitude: -75.6972,
            Tags: ['tech', 'community'],
            RegistrationCount: 34,
            DistanceKm: 2.5,
          },
        ],
        TotalCount: 1,
        Page: 1,
        PageSize: 20,
        TotalPages: 1,
      },
      error: null,
      meta: { source: 'elasticsearch' },
    });

    expect(responseBody).toEqual({
      items: [
        {
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
          distanceKm: 2.5,
          club: undefined,
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    });
  });

  it('normalizes numeric enum payloads from the backend', () => {
    let responseBody: unknown;

    service.getEvents({ page: 1, pageSize: 20 }).subscribe((response) => {
      responseBody = response.data;
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/events'));

    request.flush({
      success: true,
      message: 'ok',
      data: {
        Items: [
          {
            Id: 7,
            Name: 'Campus Mixer',
            Description: 'Meet new people',
            Location: 'Atrium',
            ImageUrls: [],
            IsPrivate: false,
            MaxParticipants: 80,
            RegisterCost: 10,
            StartTime: '2026-06-10T18:00:00Z',
            ClubId: 3,
            CreatedAt: '2026-05-02T09:00:00Z',
            Status: 0,
            Category: 5,
            Tags: [],
            RegistrationCount: 12,
          },
        ],
        TotalCount: 1,
        Page: 1,
        PageSize: 20,
        TotalPages: 1,
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
      items: [
        jasmine.objectContaining({
          id: 7,
          status: 'Upcoming',
          category: 'Social',
        }),
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    });
  });

  it('normalizes a single event payload from the backend', () => {
    let responseBody: unknown;

    service.getEvent(42).subscribe((response) => {
      responseBody = response.data;
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/events/42'));

    expect(request.request.method).toBe('GET');

    request.flush({
      success: true,
      message: 'ok',
      data: {
        Id: 42,
        Name: 'Hack Night',
        Description: 'Build things together',
        Location: 'Student Center',
        ImageUrls: ['https://example.com/poster.png'],
        IsPrivate: false,
        MaxParticipants: 120,
        RegisterCost: 0,
        StartTime: '2026-05-20T18:00:00Z',
        EndTime: '2026-05-20T21:00:00Z',
        ClubId: 7,
        CreatedAt: '2026-05-01T12:00:00Z',
        Status: 'Upcoming',
        Category: 'Workshop',
        VenueName: 'Main Hall',
        City: 'Ottawa',
        Latitude: 45.4215,
        Longitude: -75.6972,
        Tags: ['tech', 'community'],
        RegistrationCount: 34,
        Club: {
          Id: 7,
          Name: 'uOttaHack',
          Description: 'Hackathons and builder meetups',
          ClubType: 'Academic',
          ClubImage: 'https://example.com/club.png',
          MemberCount: 240,
          EventCount: 18,
          AvailableEventCount: 3,
          IsPrivate: false,
          Email: 'hello@uottahack.ca',
          Phone: '555-0101',
          Rating: 4.8,
          WebsiteUrl: 'https://uottahack.ca',
          Location: 'Ottawa',
        },
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
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
    });
  });
});
