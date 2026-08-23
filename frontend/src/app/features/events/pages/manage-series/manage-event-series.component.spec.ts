import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';

import { EventSeries, ManagedEvent } from '../../models/event.types';
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

describe('ManageEventSeriesComponent', () => {
  let component: ManageEventSeriesComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({}) }, parent: null },
        },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        {
          provide: EventSeriesService,
          useValue: jasmine.createSpyObj<EventSeriesService>('EventSeriesService', [
            'getSeries',
            'cancelSeries',
            'deleteSeries',
            'publishSeries',
          ]),
        },
      ],
    });

    component = TestBed.runInInjectionContext(() => new ManageEventSeriesComponent());
  });

  function seedOccurrences(occurrences: ManagedEvent[]): void {
    component.series = { occurrences } as unknown as EventSeries;
  }

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
