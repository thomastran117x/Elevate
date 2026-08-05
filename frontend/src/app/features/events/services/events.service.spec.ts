import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EventsService } from './events.service';
import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
} from '../../../core/api/models/api-client-error.model';

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
    expect(request.request.params.has('isPrivate')).toBeFalse();

    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('does not serialize private visibility flags for public discovery', () => {
    const unsafeParams = { page: 1, pageSize: 20, isPrivate: true } as unknown as Parameters<
      EventsService['getEvents']
    >[0];

    service.getEvents(unsafeParams).subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/events'));

    expect(request.request.method).toBe('GET');
    expect(request.request.params.has('isPrivate')).toBeFalse();

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
          lifecycleState: 'Published',
          status: 'Upcoming',
          category: 'Workshop',
          venueName: 'Main Hall',
          city: 'Ottawa',
          latitude: 45.4215,
          longitude: -75.6972,
          tags: ['tech', 'community'],
          registrationCount: 34,
          waitlistEnabled: false,
          waitlistCount: 0,
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
          lifecycleState: 'Published',
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
      lifecycleState: 'Published',
      status: 'Upcoming',
      category: 'Workshop',
      venueName: 'Main Hall',
      city: 'Ottawa',
      latitude: 45.4215,
      longitude: -75.6972,
      tags: ['tech', 'community'],
      registrationCount: 34,
      waitlistEnabled: false,
      waitlistCount: 0,
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
  it('surfaces 4xx failures as typed client errors', () => {
    let thrown: unknown;

    service.getEvent(42).subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/events/42'));
    request.flush(
      {
        success: false,
        message: 'Event not found.',
        error: { code: 'RESOURCE_NOT_FOUND' },
      },
      { status: 404, statusText: 'Not Found' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe('Event not found.');
    expect((thrown as ApiClientClientError).code).toBe('RESOURCE_NOT_FOUND');
  });

  it('collapses 5xx failures to the generic adapter error', () => {
    let thrown: unknown;

    service.getEvent(42).subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/events/42'));
    request.flush(
      {
        success: false,
        message: 'Sensitive backend failure.',
        error: { code: 'SERVER_FAILURE' },
      },
      { status: 500, statusText: 'Server Error' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
    expect((thrown as ApiClientServerError).message).toBe(GENERIC_API_ERROR_MESSAGE);
  });

  describe('camelCase payloads', () => {
    it('prefers the camelCase key wherever both casings are present', () => {
      let data: unknown;
      service.getEvents({}).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url.endsWith('/events'))
        .flush({
          success: true,
          message: 'ok',
          data: {
            items: [
              {
                id: 1,
                Id: 99,
                name: 'Camel',
                Name: 'Pascal',
                description: 'desc',
                location: 'loc',
                imageUrls: ['a.png'],
                isPrivate: true,
                maxParticipants: 40,
                registerCost: 10,
                startTime: '2026-09-01T18:00:00Z',
                endTime: '2026-09-01T21:00:00Z',
                clubId: 3,
                createdAt: '2026-08-01T00:00:00Z',
                lifecycleState: 'Published',
                status: 'Ongoing',
                category: 'Music',
                venueName: 'Hall',
                city: 'Ottawa',
                latitude: 45,
                longitude: -75,
                tags: ['free'],
                registrationCount: 5,
                waitlistEnabled: true,
                waitlistCount: 2,
                distanceKm: 1.5,
                club: {
                  id: 3,
                  name: 'Robotics',
                  description: 'Build robots',
                  clubType: 'Academic',
                  clubImage: 'c.png',
                  memberCount: 10,
                  eventCount: 2,
                  availableEventCount: 1,
                  isPrivate: false,
                  email: 'c@example.com',
                  phone: '555',
                  rating: 4.5,
                  websiteUrl: 'https://c',
                  location: 'Ottawa',
                },
              },
            ],
            totalCount: 1,
            page: 2,
            pageSize: 5,
            totalPages: 1,
          },
          error: null,
          meta: { source: 'elasticsearch' },
        });

      expect(data).toEqual(
        jasmine.objectContaining({ totalCount: 1, page: 2, pageSize: 5, totalPages: 1 }),
      );
      const item = (data as { items: Record<string, unknown>[] }).items[0];
      expect(item['id']).toBe(1);
      expect(item['name']).toBe('Camel');
      expect(item['distanceKm']).toBe(1.5);
      expect(item['club']).toEqual(
        jasmine.objectContaining({ id: 3, clubType: 'Academic', availableEventCount: 1 }),
      );
    });

    it('reads the legacy misspelled AvaliableEventCount on the host club', () => {
      let data: unknown;
      service.getEvent(1).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url.endsWith('/events/1'))
        .flush({
          Success: true,
          Message: 'ok',
          Data: { Id: 1, Club: { Id: 3, AvaliableEventCount: 7, Clubtype: 'Gaming' } },
        });

      expect((data as { club: { availableEventCount: number; clubType: string } }).club).toEqual(
        jasmine.objectContaining({ availableEventCount: 7, clubType: 'Gaming' }),
      );
    });

    it('defaults every field of a bare event payload', () => {
      let data: Record<string, unknown> | null = null;
      service.getEvent(1).subscribe((response) => (data = response.data as never));

      httpMock
        .expectOne((req) => req.url.endsWith('/events/1'))
        .flush({
          success: true,
          message: 'ok',
          data: {},
          error: null,
          meta: null,
        });

      expect(data).toEqual(
        jasmine.objectContaining({
          id: 0,
          name: '',
          description: '',
          location: '',
          imageUrls: [],
          isPrivate: false,
          maxParticipants: 0,
          registerCost: 0,
          startTime: '',
          clubId: 0,
          createdAt: '',
          lifecycleState: 'Published',
          status: 'Upcoming',
          category: 'Other',
          tags: [],
          registrationCount: 0,
          waitlistEnabled: false,
          waitlistCount: 0,
          club: undefined,
        }),
      );
    });

    it('defaults the paging metadata of a bare page payload', () => {
      let data: unknown;
      service.getEvents({}).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url.endsWith('/events'))
        .flush({
          success: true,
          message: 'ok',
          data: {},
          error: null,
          meta: null,
        });

      expect(data).toEqual({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 });
    });

    it('leaves data null when the envelope carries none', () => {
      let data: unknown = 'untouched';
      service.getEvents({}).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url.endsWith('/events'))
        .flush({ success: true, message: 'ok', data: null, error: null, meta: null });

      expect(data).toBeNull();
    });

    it('falls back for out-of-range numeric enums', () => {
      let data: Record<string, unknown> | null = null;
      service.getEvent(1).subscribe((response) => (data = response.data as never));

      httpMock
        .expectOne((req) => req.url.endsWith('/events/1'))
        .flush({
          success: true,
          message: 'ok',
          data: { Id: 1, Status: 99, LifecycleState: 99, Category: 99, Club: { Clubtype: 99 } },
          error: null,
          meta: null,
        });

      expect(data).toEqual(
        jasmine.objectContaining({
          status: 'Upcoming',
          lifecycleState: 'Published',
          category: 'Other',
        }),
      );
      expect((data as unknown as { club: { clubType: string } }).club.clubType).toBe('Other');
    });

    it('falls back for unrecognized string enums', () => {
      let data: Record<string, unknown> | null = null;
      service.getEvent(1).subscribe((response) => (data = response.data as never));

      httpMock
        .expectOne((req) => req.url.endsWith('/events/1'))
        .flush({
          success: true,
          message: 'ok',
          data: {
            Id: 1,
            Status: 'Vibes',
            LifecycleState: 'Zombie',
            Category: 'Interpretive Dance',
            Club: { Clubtype: 'Underwater Basketry' },
          },
          error: null,
          meta: null,
        });

      expect(data).toEqual(
        jasmine.objectContaining({
          status: 'Upcoming',
          lifecycleState: 'Published',
          category: 'Other',
        }),
      );
      expect((data as unknown as { club: { clubType: string } }).club.clubType).toBe('Other');
    });
  });

  describe('getEventsByClub', () => {
    it('serializes only the filters that are set', () => {
      service
        .getEventsByClub(3, { status: 'Upcoming', page: 2, pageSize: 5, search: '  robotics  ' })
        .subscribe();

      const request = httpMock.expectOne((req) => req.url.endsWith('/events/clubs/3'));
      expect(request.request.method).toBe('GET');
      expect(request.request.params.get('status')).toBe('Upcoming');
      expect(request.request.params.get('page')).toBe('2');
      expect(request.request.params.get('pageSize')).toBe('5');
      expect(request.request.params.get('search')).toBe('robotics');
      request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
    });

    it('omits blank, zero and absent filters', () => {
      service.getEventsByClub(3, { page: 0, pageSize: 0, search: '   ' }).subscribe();

      const request = httpMock.expectOne((req) => req.url.endsWith('/events/clubs/3'));
      expect(request.request.params.keys()).toEqual([]);
      request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
    });

    it('defaults to no filters at all', () => {
      service.getEventsByClub(3).subscribe();

      const request = httpMock.expectOne((req) => req.url.endsWith('/events/clubs/3'));
      expect(request.request.params.keys()).toEqual([]);
      request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
    });

    it('normalizes the page it gets back', () => {
      let data: unknown;
      service.getEventsByClub(3).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url.endsWith('/events/clubs/3'))
        .flush({
          Success: true,
          Message: 'ok',
          Data: { Items: [{ Id: 5, Name: 'Kickoff' }], TotalCount: 1 },
        });

      expect((data as { items: { id: number }[] }).items[0].id).toBe(5);
    });
  });
});
