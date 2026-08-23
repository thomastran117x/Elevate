import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ALL_LIFECYCLE_STATES, EventSeries, ManagedEvent } from '../../models/event.types';
import { EventSeriesService } from '../../services/event-series.service';
import { ManageEventSeriesComponent } from './manage-event-series.component';

function occurrence(overrides: Partial<ManagedEvent> = {}): ManagedEvent {
  return {
    id: 1,
    imageUrls: [],
    isPrivate: false,
    registerCost: 0,
    clubId: 3,
    currentVersionNumber: 1,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    lifecycleState: 'Published',
    category: 'Other',
    tags: [],
    registrationCount: 0,
    waitlistEnabled: false,
    waitlistCount: 0,
    publishReady: true,
    publishIssues: [],
    availableTransitions: [],
    ...overrides,
  } as ManagedEvent;
}

function inDays(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString();
}

function seriesEnvelope(occurrences: ManagedEvent[], clubId = 3) {
  return of({
    success: true,
    message: 'ok',
    data: { id: 9, clubId, occurrences },
  }) as never;
}

function bulkEnvelope(affectedCount: number) {
  return of({
    success: true,
    message: 'ok',
    data: { affectedCount, affectedEventIds: [], skipped: [], retimedWithRegistrations: [] },
  }) as never;
}

describe('ManageEventSeriesComponent', () => {
  let component: ManageEventSeriesComponent;
  let route: {
    snapshot: { paramMap: ReturnType<typeof convertToParamMap> };
    parent: { snapshot: { paramMap: ReturnType<typeof convertToParamMap> } } | null;
  };

  beforeEach(() => {
    route = { snapshot: { paramMap: convertToParamMap({}) }, parent: null };

    TestBed.configureTestingModule({
      providers: [
        { provide: ActivatedRoute, useValue: route },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        {
          provide: EventSeriesService,
          useValue: jasmine.createSpyObj<EventSeriesService>('EventSeriesService', [
            'getSeries',
            'cancelSeries',
            'deleteSeries',
            'publishSeries',
            'extendSeries',
            'detachOccurrence',
            'describeSkipped',
          ]),
        },
      ],
    });

    component = TestBed.runInInjectionContext(() => new ManageEventSeriesComponent());
  });

  function seedOccurrences(occurrences: ManagedEvent[]): void {
    component.series = { occurrences } as unknown as EventSeries;
  }

  function initWith(params: Record<string, string>, parentParams?: Record<string, string>): void {
    route.snapshot.paramMap = convertToParamMap(params);
    route.parent = parentParams
      ? { snapshot: { paramMap: convertToParamMap(parentParams) } }
      : null;

    component.ngOnInit();
  }

  describe('initialisation', () => {
    it('loads the series named in the route', () => {
      const service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;
      service.getSeries.and.returnValue(seriesEnvelope([occurrence({ id: 1 })]));

      initWith({ clubId: '3', seriesId: '9' });

      expect(service.getSeries).toHaveBeenCalledOnceWith(9);
      expect(component.loading).toBeFalse();
      expect(component.occurrences.length).toBe(1);
    });

    it('takes the club id from the parent route when the child route lacks it', () => {
      const service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;
      // A failed load leaves the route-derived value in place, which is the only window in
      // which it matters -- a successful load replaces it with the series' own club id.
      service.getSeries.and.returnValue(throwError(() => ({})) as never);

      initWith({ seriesId: '9' }, { clubId: '77' });

      expect(component.clubId).toBe(77);
    });

    it('adopts the club id the loaded series reports', () => {
      const service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;
      service.getSeries.and.returnValue(seriesEnvelope([], 42));

      initWith({ clubId: '3', seriesId: '9' });

      expect(component.clubId).toBe(42);
    });

    it('refuses a route with no usable series id', () => {
      const service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;

      initWith({ clubId: '3', seriesId: 'not-a-number' });

      expect(service.getSeries).not.toHaveBeenCalled();
      expect(component.error).toBe('A valid series ID is required.');
      expect(component.loading).toBeFalse();
    });

    it('surfaces a failed load', () => {
      const service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;
      service.getSeries.and.returnValue(
        throwError(() => ({ error: { message: 'Series is gone.' } })) as never,
      );

      initWith({ clubId: '3', seriesId: '9' });

      expect(component.error).toBe('Series is gone.');
      expect(component.loading).toBeFalse();
    });
  });

  describe('summary getters', () => {
    it('counts only drafts', () => {
      seedOccurrences([
        occurrence({ id: 1, lifecycleState: 'Draft' }),
        occurrence({ id: 2, lifecycleState: 'Draft' }),
        occurrence({ id: 3, lifecycleState: 'Published' }),
      ]);

      expect(component.draftCount).toBe(2);
    });

    it('is empty until a series has loaded', () => {
      expect(component.occurrences).toEqual([]);
      expect(component.draftCount).toBe(0);
      expect(component.ruleSummary).toBe('');
    });

    it('describes a simple weekly rule', () => {
      component.series = {
        occurrences: [],
        rule: {
          frequency: 'Weekly',
          interval: 1,
          endMode: 'Count',
          occurrenceCount: 8,
          timeZoneId: 'America/Toronto',
        },
      } as unknown as EventSeries;

      expect(component.ruleSummary).toBe('Repeats weekly, 8 occurrences, in America/Toronto');
    });

    it('spells out a multi-week interval and an end date', () => {
      component.series = {
        occurrences: [],
        rule: {
          frequency: 'Weekly',
          interval: 3,
          endMode: 'Date',
          endLocalDate: '2026-12-01',
          timeZoneId: 'America/Toronto',
        },
      } as unknown as EventSeries;

      expect(component.ruleSummary).toBe(
        'Repeats every 3 weeks, until 2026-12-01, in America/Toronto',
      );
    });

    it('handles daily and monthly intervals', () => {
      const rule = (frequency: string) =>
        ({
          occurrences: [],
          rule: {
            frequency,
            interval: 2,
            endMode: 'Count',
            occurrenceCount: 4,
            timeZoneId: 'UTC',
          },
        }) as unknown as EventSeries;

      component.series = rule('Daily');
      expect(component.ruleSummary).toContain('every 2 days');

      component.series = rule('Monthly');
      expect(component.ruleSummary).toContain('every 2 months');
    });

    it('maps every lifecycle state to a badge', () => {
      for (const state of ALL_LIFECYCLE_STATES) {
        expect(component.lifecycleBadge(state)).toContain('border');
      }
    });
  });

  describe('confirmed bulk actions', () => {
    let service: jasmine.SpyObj<EventSeriesService>;

    beforeEach(() => {
      service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;
      component.seriesId = 9;
      component.clubId = 3;
      service.getSeries.and.returnValue(seriesEnvelope([]));
      service.describeSkipped.and.returnValue(null);
    });

    it('publishes the series and reports how many changed', () => {
      service.publishSeries.and.returnValue(bulkEnvelope(3));
      seedOccurrences([occurrence({ id: 1, lifecycleState: 'Draft' })]);

      component.askPublishAll();
      component.onBulkActionResolved(true);

      expect(service.publishSeries).toHaveBeenCalledOnceWith(9);
      expect(component.successMessage).toBe('3 occurrences published.');
      expect(component.working).toBeFalse();
    });

    it('uses the singular when exactly one occurrence changed', () => {
      service.cancelSeries.and.returnValue(bulkEnvelope(1));
      seedOccurrences([occurrence({ id: 1, startTime: inDays(2) })]);

      component.askCancelSeries();
      component.onBulkActionResolved(true);

      expect(service.cancelSeries).toHaveBeenCalledOnceWith(9, true);
      expect(component.successMessage).toBe('1 occurrence cancelled.');
    });

    it('passes a partial-success note through to the banner', () => {
      service.publishSeries.and.returnValue(bulkEnvelope(2));
      service.describeSkipped.and.returnValue('1 occurrence was skipped.');

      component.askPublishAll();
      component.onBulkActionResolved(true);

      expect(component.notice).toBe('1 occurrence was skipped.');
    });

    it('navigates away after deleting, handing the id to the events tab', () => {
      const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
      service.deleteSeries.and.returnValue(
        of({ success: true, message: 'ok', data: { seriesId: 9 } }) as never,
      );

      component.askDeleteSeries();
      component.onBulkActionResolved(true);

      expect(service.deleteSeries).toHaveBeenCalledOnceWith(9, 'FutureDrafts');
      expect(router.navigate).toHaveBeenCalledWith(['/clubs', 3, 'manage', 'events'], {
        state: { seriesDeleted: 9 },
      });
    });

    it('reports a rejected bulk action and stops working', () => {
      service.publishSeries.and.returnValue(
        throwError(() => ({ error: { message: 'Nothing to publish.' } })) as never,
      );

      component.askPublishAll();
      component.onBulkActionResolved(true);

      expect(component.error).toBe('Nothing to publish.');
      expect(component.working).toBeFalse();
    });

    it('falls back to a generic message when the failure carries none', () => {
      service.deleteSeries.and.returnValue(throwError(() => ({})) as never);

      component.askDeleteSeries();
      component.onBulkActionResolved(true);

      expect(component.error).toBe('We could not delete the series.');
    });

    it('does nothing when resolved with no action pending', () => {
      component.pendingBulkAction = null;
      component.onBulkActionResolved(true);

      expect(service.publishSeries).not.toHaveBeenCalled();
      expect(service.cancelSeries).not.toHaveBeenCalled();
      expect(service.deleteSeries).not.toHaveBeenCalled();
    });
  });

  describe('per-occurrence and sizing actions', () => {
    let service: jasmine.SpyObj<EventSeriesService>;

    beforeEach(() => {
      service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;
      component.seriesId = 9;
      service.getSeries.and.returnValue(seriesEnvelope([]));
    });

    it('extends the series and reports the new size', () => {
      service.extendSeries.and.returnValue(
        of({
          success: true,
          message: 'ok',
          data: { occurrences: [occurrence({ id: 1 }), occurrence({ id: 2 })] },
        }) as never,
      );

      component.extend(6);

      expect(service.extendSeries).toHaveBeenCalledOnceWith(9, { occurrenceCount: 6 });
      expect(component.successMessage).toBe('The series now has 2 occurrences.');
      expect(component.working).toBeFalse();
    });

    it('surfaces a failed extend', () => {
      service.extendSeries.and.returnValue(throwError(() => ({})) as never);

      component.extend(6);

      expect(component.error).toBe('We could not extend the series.');
    });

    it('detaches an occurrence and reloads', () => {
      service.detachOccurrence.and.returnValue(
        of({ success: true, message: 'ok', data: {} }) as never,
      );

      component.detach(occurrence({ id: 4 }));

      expect(service.detachOccurrence).toHaveBeenCalledOnceWith(4);
      expect(component.successMessage).toBe('That occurrence is now a standalone event.');
      expect(service.getSeries).toHaveBeenCalled();
    });

    it('surfaces a failed detach', () => {
      service.detachOccurrence.and.returnValue(
        throwError(() => ({ error: { Message: 'Occurrence is booked.' } })) as never,
      );

      component.detach(occurrence({ id: 4 }));

      expect(component.error).toBe('Occurrence is booked.');
      expect(component.working).toBeFalse();
    });
  });

  describe('cancel prompt', () => {
    it('counts only occurrences the request will actually change', () => {
      // cancelSeries sends futureOnly, and the backend skips anything already started, so a
      // prompt that counted past occurrences would overstate what is about to happen.
      seedOccurrences([
        occurrence({ id: 1, lifecycleState: 'Published', startTime: inDays(3) }),
        occurrence({ id: 2, lifecycleState: 'Paused', startTime: inDays(5) }),
        occurrence({ id: 3, lifecycleState: 'Published', startTime: inDays(-2) }),
        occurrence({ id: 4, lifecycleState: 'Paused', startTime: inDays(-9) }),
      ]);

      component.askCancelSeries();

      expect(component.pendingBulkAction!.impacts).toContain('2 occurrences will be cancelled.');
    });

    it('ignores states that cannot be cancelled at all', () => {
      seedOccurrences([
        occurrence({ id: 1, lifecycleState: 'Draft', startTime: inDays(3) }),
        occurrence({ id: 2, lifecycleState: 'Cancelled', startTime: inDays(3) }),
        occurrence({ id: 3, lifecycleState: 'Archived', startTime: inDays(3) }),
        occurrence({ id: 4, lifecycleState: 'Published', startTime: inDays(3) }),
      ]);

      component.askCancelSeries();

      expect(component.pendingBulkAction!.impacts).toContain('1 occurrence will be cancelled.');
    });

    it('skips occurrences with no start time rather than assuming they are upcoming', () => {
      seedOccurrences([occurrence({ id: 1, lifecycleState: 'Published', startTime: undefined })]);

      component.askCancelSeries();

      expect(component.pendingBulkAction!.impacts).toContain('0 occurrences will be cancelled.');
    });

    it('names the people affected when anyone has registered', () => {
      seedOccurrences([
        occurrence({
          id: 1,
          lifecycleState: 'Published',
          startTime: inDays(3),
          registrationCount: 4,
        }),
        occurrence({ id: 2, lifecycleState: 'Published', startTime: inDays(4) }),
      ]);

      component.askCancelSeries();

      expect(component.pendingBulkAction!.impacts[0]).toContain(
        '1 occurrence has people registered',
      );
    });

    it('is destructive but reversible, and reaches the service only once confirmed', () => {
      seedOccurrences([occurrence({ id: 1, startTime: inDays(3) })]);
      const service = TestBed.inject(EventSeriesService) as jasmine.SpyObj<EventSeriesService>;

      component.askCancelSeries();
      expect(component.pendingBulkAction!.tone).toBe('danger');
      expect(component.pendingBulkAction!.reversibleNote).toContain('reinstated');
      expect(service.cancelSeries).not.toHaveBeenCalled();

      component.onBulkActionResolved(false);
      expect(service.cancelSeries).not.toHaveBeenCalled();
      expect(component.pendingBulkAction).toBeNull();
    });
  });

  describe('delete prompt', () => {
    it('requires a typed confirmation, being the one unrecoverable action here', () => {
      seedOccurrences([occurrence()]);

      component.askDeleteSeries();

      expect(component.pendingBulkAction!.requireTypedConfirmation).toBe('DELETE');
      expect(component.pendingBulkAction!.reversibleNote).toBeNull();
    });
  });
});
