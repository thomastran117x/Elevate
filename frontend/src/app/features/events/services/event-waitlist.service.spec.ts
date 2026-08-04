import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EventWaitlistService } from './event-waitlist.service';

describe('EventWaitlistService', () => {
  let service: EventWaitlistService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [EventWaitlistService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(EventWaitlistService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('posts to the waitlist endpoint with credentials and normalizes the entry', () => {
    let position = 0;
    service.join(42, { notes: 'keen' }).subscribe((entry) => (position = entry.position));

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/waitlist'));
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBeTrue();
    expect(request.request.body).toEqual({ notes: 'keen' });

    request.flush({
      success: true,
      message: 'ok',
      data: { id: 9, eventId: 42, userId: 7, position: 3, status: 'Waiting', joinedAtUtc: 'now' },
      error: null,
      meta: null,
    });

    expect(position).toBe(3);
  });

  it('normalizes PascalCase entry payloads', () => {
    let status = '';
    let email: string | null | undefined = '';
    service.join(42).subscribe((entry) => {
      status = entry.status;
      email = entry.userEmail;
    });

    httpMock
      .expectOne((r) => r.url.includes('/events/42/waitlist'))
      .flush({
        Data: {
          Id: 1,
          EventId: 42,
          UserId: 7,
          Position: 1,
          Status: 'Promoted',
          UserEmail: 'a@b.c',
        },
      });

    expect(status).toBe('Promoted');
    expect(email).toBe('a@b.c');
  });

  it('falls back to Waiting for an unrecognized status', () => {
    let status = '';
    service.join(42).subscribe((entry) => (status = entry.status));

    httpMock
      .expectOne((r) => r.url.includes('/events/42/waitlist'))
      .flush({ data: { id: 1, status: 'something-unexpected' } });

    expect(status).toBe('Waiting');
  });

  it('deletes to leave the waitlist', () => {
    service.leave(42).subscribe();

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/waitlist'));
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('reads my status and defaults missing fields', () => {
    let onWaitlist = true;
    let count = -1;
    service.getMyStatus(42).subscribe((status) => {
      onWaitlist = status.onWaitlist;
      count = status.waitlistCount;
    });

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/waitlist/me'));
    expect(request.request.method).toBe('GET');
    request.flush({ data: {} });

    expect(onWaitlist).toBeFalse();
    expect(count).toBe(0);
  });

  it('reads the organizer roster with paging and total count from meta', () => {
    let total = 0;
    let entries = 0;
    service.getEventWaitlist(42, 2, 10).subscribe((page) => {
      total = page.totalCount;
      entries = page.entries.length;
    });

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/waitlist'));
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('10');

    request.flush({
      data: [
        { id: 1, position: 11 },
        { id: 2, position: 12 },
      ],
      meta: { totalCount: 25 },
    });

    expect(entries).toBe(2);
    expect(total).toBe(25);
  });

  it('promotes the next person in line', () => {
    let promoted = 0;
    service.promoteNext(42).subscribe((result) => (promoted = result.promotedCount));

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/waitlist/promote'));
    expect(request.request.method).toBe('POST');
    request.flush({ data: { promotedCount: 2, promotedUserIds: [5, 6] } });

    expect(promoted).toBe(2);
  });

  it('lists my waitlisted events', () => {
    let count = 0;
    service.getMine().subscribe((items) => (count = items.length));

    const request = httpMock.expectOne((r) => r.url.includes('/events/me/waitlisted'));
    expect(request.request.method).toBe('GET');
    request.flush({ data: [{ entryId: 1, position: 2, joinedAtUtc: 'now', event: { id: 42 } }] });

    expect(count).toBe(1);
  });
});
