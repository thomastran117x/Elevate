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
});
