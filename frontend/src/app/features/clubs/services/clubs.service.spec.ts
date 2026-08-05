import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ClubsService } from './clubs.service';
import { ApiClientClientError } from '../../../core/api/models/api-client-error.model';

describe('ClubsService', () => {
  let service: ClubsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ClubsService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(ClubsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('serializes supported club search filters', () => {
    service
      .getClubs({
        search: '  robotics  ',
        clubType: 'Academic',
        sortBy: 'Members',
        page: 2,
        pageSize: 12,
      })
      .subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs'));
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('search')).toBe('robotics');
    expect(request.request.params.get('clubType')).toBe('Academic');
    expect(request.request.params.get('sortBy')).toBe('Members');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('12');
    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('normalizes PascalCase club payloads from the backend', () => {
    let responseBody: unknown;

    service.getClub(7).subscribe((response) => {
      responseBody = response.data;
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs/7'));
    request.flush({
      success: true,
      message: 'ok',
      data: {
        Id: 7,
        OwnerId: 99,
        Name: 'Robotics Club',
        Description: 'Build robots together',
        Clubtype: 'Academic',
        ClubImage: 'https://example.com/club.png',
        MemberCount: 45,
        EventCount: 8,
        AvaliableEventCount: 3,
        MaxMemberCount: 80,
        IsPrivate: false,
        Rating: 4.7,
        Location: 'Ottawa',
        Phone: '555-1111',
        Email: 'robotics@example.com',
        WebsiteUrl: 'https://robotics.example.com',
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
      id: 7,
      ownerId: 99,
      name: 'Robotics Club',
      description: 'Build robots together',
      clubType: 'Academic',
      clubImage: 'https://example.com/club.png',
      bannerImage: null,
      galleryImages: [],
      memberCount: 45,
      eventCount: 8,
      availableEventCount: 3,
      maxMemberCount: 80,
      isPrivate: false,
      rating: 4.7,
      location: 'Ottawa',
      phone: '555-1111',
      email: 'robotics@example.com',
      websiteUrl: 'https://robotics.example.com',
      currentVersionNumber: 0,
      isOwner: false,
      isManager: false,
      isVolunteer: false,
      canManage: false,
    });
  });

  it('surfaces 4xx failures as typed client errors', () => {
    let thrown: unknown;

    service.getClub(7).subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs/7'));
    request.flush(
      {
        success: false,
        message: 'Club not found.',
        error: { code: 'RESOURCE_NOT_FOUND' },
      },
      { status: 404, statusText: 'Not Found' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe('Club not found.');
    expect((thrown as ApiClientClientError).code).toBe('RESOURCE_NOT_FOUND');
  });

  it('omits blank and zero filters', () => {
    service.getClubs({ search: '   ', page: 0, pageSize: 0 }).subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs'));
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('defaults to no filters at all', () => {
    service.getClubs().subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs'));
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('normalizes a PascalCase clubs page and applies the paging defaults', () => {
    let data: unknown;
    service.getClubs().subscribe((response) => (data = response.data));

    httpMock
      .expectOne((req) => req.url.endsWith('/clubs'))
      .flush({
        Success: true,
        Message: 'ok',
        Data: { Items: [{ Id: 1, Name: 'Robotics' }] },
      });

    expect(data).toEqual(
      jasmine.objectContaining({
        totalCount: 0,
        page: 1,
        pageSize: 20,
        totalPages: 0,
        items: [jasmine.objectContaining({ id: 1, name: 'Robotics' })],
      }),
    );
  });

  it('leaves data null when either endpoint returns an empty envelope', () => {
    let list: unknown = 'untouched';
    service.getClubs().subscribe((response) => (list = response.data));
    httpMock
      .expectOne((req) => req.url.endsWith('/clubs'))
      .flush({ success: true, message: 'ok', data: null, error: null, meta: null });
    expect(list).toBeNull();

    let single: unknown = 'untouched';
    service.getClub(7).subscribe((response) => (single = response.data));
    httpMock
      .expectOne((req) => req.url.endsWith('/clubs/7'))
      .flush({ success: true, message: 'ok', data: null, error: null, meta: null });
    expect(single).toBeNull();
  });

  it('prefers camelCase club fields and falls back to Other for an unknown type', () => {
    let data: unknown;
    service.getClub(7).subscribe((response) => (data = response.data));

    httpMock
      .expectOne((req) => req.url.endsWith('/clubs/7'))
      .flush({
        success: true,
        message: 'ok',
        data: {
          id: 7,
          Id: 99,
          ownerId: 1,
          name: 'Camel',
          Name: 'Pascal',
          description: 'd',
          clubtype: 'Underwater Basketry',
          clubImage: 'c.png',
          bannerImage: 'b.png',
          galleryImages: ['g.png'],
          memberCount: 5,
          eventCount: 2,
          availableEventCount: 1,
          maxMemberCount: 50,
          isPrivate: true,
          rating: 4,
          location: 'Ottawa',
          phone: '555',
          email: 'c@example.com',
          websiteUrl: 'https://c',
          currentVersionNumber: 3,
          isOwner: true,
          isManager: true,
          isVolunteer: true,
          canManage: true,
        },
        error: null,
        meta: null,
      });

    expect(data).toEqual(
      jasmine.objectContaining({
        id: 7,
        name: 'Camel',
        clubType: 'Other',
        bannerImage: 'b.png',
        galleryImages: ['g.png'],
        currentVersionNumber: 3,
        canManage: true,
      }),
    );
  });

  it('joins a club with an empty body', () => {
    service.joinClub(7).subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs/7/join'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({});
    expect(request.request.withCredentials).toBeTrue();
    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('leaves a club', () => {
    service.leaveClub(7).subscribe();

    const request = httpMock.expectOne((req) => req.url.endsWith('/clubs/7/join'));
    expect(request.request.method).toBe('DELETE');
    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  describe('getMembershipStatus', () => {
    function flush(body: Record<string, unknown>): boolean | undefined {
      let result: boolean | undefined;
      service.getMembershipStatus(7).subscribe((value) => (result = value));
      httpMock.expectOne((req) => req.url.endsWith('/clubs/7/members/me')).flush(body);
      return result;
    }

    it('reads the camelCase flag', () => {
      expect(
        flush({ success: true, message: 'ok', data: { isMember: true }, error: null, meta: null }),
      ).toBeTrue();
    });

    it('reads the PascalCase flag', () => {
      expect(flush({ Success: true, Message: 'ok', Data: { IsMember: true } })).toBeTrue();
    });

    it('defaults to false for an empty payload', () => {
      expect(
        flush({ success: true, message: 'ok', data: {}, error: null, meta: null }),
      ).toBeFalse();
    });

    it('defaults to false when the envelope carries no data', () => {
      expect(
        flush({ success: true, message: 'ok', data: null, error: null, meta: null }),
      ).toBeFalse();
    });
  });
});
