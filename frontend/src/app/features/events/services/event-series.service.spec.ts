import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EventSeriesService } from './event-series.service';
import { RecurrenceRule, SeriesBulkResult } from '../models/event.types';

describe('EventSeriesService', () => {
  let service: EventSeriesService;
  let httpMock: HttpTestingController;

  const rule: RecurrenceRule = {
    frequency: 'Weekly',
    interval: 1,
    byWeekdays: [2],
    monthlyDayPolicy: 'ClampToMonthEnd',
    startLocalDateTime: '2026-03-03T19:00:00',
    durationMinutes: 120,
    timeZoneId: 'America/New_York',
    endMode: 'Count',
    occurrenceCount: 4,
  };

  const envelope = <T>(data: T) => ({
    success: true,
    message: 'ok',
    data,
    error: null,
    meta: null,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [EventSeriesService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(EventSeriesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('previews a rule against the club endpoint without mutating the wall-clock start', () => {
    let count = 0;
    service
      .previewSeries(4, rule)
      .subscribe((response) => (count = response.data!.occurrenceCount));

    const request = httpMock.expectOne((r) => r.url.includes('/events/clubs/4/series/preview'));
    expect(request.request.method).toBe('POST');

    // The start must reach the API exactly as typed — no `Z`, no offset, no re-zoning.
    expect(request.request.body.recurrence.startLocalDateTime).toBe('2026-03-03T19:00:00');
    expect(request.request.body.recurrence.timeZoneId).toBe('America/New_York');

    request.flush(
      envelope({
        timeZoneId: 'America/New_York',
        occurrenceCount: 4,
        occurrences: [],
        warnings: [],
      }),
    );

    expect(count).toBe(4);
  });

  it('creates a series from a draft event', () => {
    service.createSeries(11, rule).subscribe();

    const request = httpMock.expectOne((r) => r.url.endsWith('/events/11/series'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ recurrence: rule });

    request.flush(envelope({ id: 3, occurrences: [] }));
  });

  it('fetches a series and a club listing', () => {
    service.getSeries(3).subscribe();
    const get = httpMock.expectOne((r) => r.url.endsWith('/events/series/3'));
    expect(get.request.method).toBe('GET');
    get.flush(envelope({ id: 3 }));

    service.getClubSeries(4, 2, 10).subscribe();
    const list = httpMock.expectOne((r) => r.url.endsWith('/events/clubs/4/series'));
    expect(list.request.method).toBe('GET');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('pageSize')).toBe('10');
    list.flush(envelope({ items: [], totalCount: 0, page: 2, pageSize: 10, totalPages: 0 }));
  });

  it('extends, publishes and cancels a series', () => {
    service.extendSeries(3, { occurrenceCount: 8 }).subscribe();
    const extend = httpMock.expectOne((r) => r.url.endsWith('/events/series/3/extend'));
    expect(extend.request.body).toEqual({ occurrenceCount: 8 });
    extend.flush(envelope({ id: 3 }));

    service.publishSeries(3).subscribe();
    const publish = httpMock.expectOne((r) => r.url.endsWith('/events/series/3/publish'));
    expect(publish.request.method).toBe('POST');
    publish.flush(envelope({ seriesId: 3, affectedCount: 3 }));

    service.cancelSeries(3).subscribe();
    const cancel = httpMock.expectOne((r) => r.url.endsWith('/events/series/3/cancel'));
    expect(cancel.request.body).toEqual({ futureOnly: true });
    cancel.flush(envelope({ seriesId: 3, affectedCount: 3 }));
  });

  it('patches future occurrences from a pivot', () => {
    service.updateFutureOccurrences(3, { fromEventId: 12, location: 'New Hall' }).subscribe();

    const request = httpMock.expectOne((r) => r.url.endsWith('/events/series/3/occurrences'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ fromEventId: 12, location: 'New Hall' });

    request.flush(envelope({ seriesId: 3, affectedCount: 2 }));
  });

  it('sends the delete scope in the request body', () => {
    service.deleteSeries(3, 'AllUnregistered').subscribe();

    const request = httpMock.expectOne((r) => r.url.endsWith('/events/series/3'));
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toEqual({ scope: 'AllUnregistered' });

    request.flush(envelope({ seriesId: 3, affectedCount: 0 }));
  });

  it('detaches an occurrence', () => {
    service.detachOccurrence(12).subscribe();

    const request = httpMock.expectOne((r) => r.url.endsWith('/events/12/series/detach'));
    expect(request.request.method).toBe('POST');

    request.flush(envelope({ id: 12, seriesId: null }));
  });

  describe('describeSkipped', () => {
    const result = (skipped: SeriesBulkResult['skipped']): SeriesBulkResult => ({
      seriesId: 3,
      affectedCount: 1,
      affectedEventIds: [1],
      skipped,
      retimedWithRegistrations: [],
    });

    it('returns null when nothing was skipped', () => {
      expect(service.describeSkipped(result([]))).toBeNull();
    });

    it('summarizes skipped occurrences with their reasons', () => {
      const summary = service.describeSkipped(
        result([
          {
            eventId: 2,
            occurrenceIndex: 1,
            reason: 'capacity-below-registrations',
            details: ['Capacity is below the people already registered.'],
          },
        ]),
      );

      expect(summary).toContain('1 occurrence was left unchanged');
      expect(summary).toContain('already registered');
    });

    it('de-duplicates identical reasons across occurrences', () => {
      const summary = service.describeSkipped(
        result([
          { eventId: 2, occurrenceIndex: 1, reason: 'r', details: ['Same reason.'] },
          { eventId: 3, occurrenceIndex: 2, reason: 'r', details: ['Same reason.'] },
        ]),
      );

      expect(summary).toContain('2 occurrences were left unchanged');
      expect(summary!.match(/Same reason\./g)?.length).toBe(1);
    });
  });
});
