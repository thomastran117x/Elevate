import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ALL_LIFECYCLE_STATES, ManagedEvent } from '../../../../events/models/event.types';
import { EventsManagementService } from '../../../../events/services/events-management.service';
import { EventsTabComponent } from './events-tab.component';

function managedEvent(overrides: Partial<ManagedEvent> = {}): ManagedEvent {
  return {
    id: 1,
    name: 'Board Game Night',
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

function pagedEnvelope(items: ManagedEvent[]) {
  return of({
    success: true,
    message: 'ok',
    data: {
      items,
      totalCount: items.length,
      page: 1,
      pageSize: 9,
      totalPages: 1,
    },
  }) as never;
}

describe('EventsTabComponent', () => {
  let component: EventsTabComponent;
  let management: jasmine.SpyObj<EventsManagementService>;

  beforeEach(() => {
    management = jasmine.createSpyObj<EventsManagementService>('EventsManagementService', [
      'getManageableEvents',
      'runTransition',
    ]);
    management.getManageableEvents.and.returnValue(pagedEnvelope([]));

    TestBed.configureTestingModule({
      providers: [
        EventsTabComponent,
        { provide: EventsManagementService, useValue: management },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({}) },
            parent: { snapshot: { paramMap: convertToParamMap({ clubId: '3' }) } },
          },
        },
      ],
    });

    component = TestBed.inject(EventsTabComponent);
    component.clubId = 3;
  });

  function transitionReturns(state: ManagedEvent['lifecycleState']): void {
    management.runTransition.and.returnValue(
      of({
        success: true,
        message: 'ok',
        data: managedEvent({ id: 1, lifecycleState: state }),
      }) as never,
    );
  }

  describe('initialisation', () => {
    it('loads the club named on the parent route', () => {
      management.getManageableEvents.and.returnValue(
        pagedEnvelope([managedEvent({ id: 1 }), managedEvent({ id: 2 })]),
      );

      component.ngOnInit();

      expect(management.getManageableEvents).toHaveBeenCalledOnceWith(3, {
        lifecycleState: null,
        page: 1,
        pageSize: 9,
        search: undefined,
      });
      expect(component.events.length).toBe(2);
      expect(component.totalCount).toBe(2);
      expect(component.loading).toBeFalse();
    });

    it('refuses a route with no usable club id', () => {
      TestBed.resetTestingModule();
      const svc = jasmine.createSpyObj<EventsManagementService>('EventsManagementService', [
        'getManageableEvents',
        'runTransition',
      ]);
      TestBed.configureTestingModule({
        providers: [
          EventsTabComponent,
          { provide: EventsManagementService, useValue: svc },
          {
            provide: ActivatedRoute,
            useValue: { snapshot: { paramMap: convertToParamMap({}) }, parent: null },
          },
        ],
      });

      const bare = TestBed.inject(EventsTabComponent);
      bare.ngOnInit();

      expect(svc.getManageableEvents).not.toHaveBeenCalled();
      expect(bare.error).toBe('A valid club ID is required.');
      expect(bare.loading).toBeFalse();
    });

    it('surfaces a failed load', () => {
      management.getManageableEvents.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ status: 500, error: { message: 'Elasticsearch is down.' } }),
        ) as never,
      );

      component.ngOnInit();

      expect(component.error).toBe('Elasticsearch is down.');
      expect(component.loading).toBeFalse();
    });

    it('defaults an empty payload rather than emitting undefined', () => {
      management.getManageableEvents.and.returnValue(
        of({ success: true, message: 'ok', data: null }) as never,
      );

      component.ngOnInit();

      expect(component.events).toEqual([]);
      expect(component.totalCount).toBe(0);
    });
  });

  describe('filtering, paging and search', () => {
    beforeEach(() => {
      management.getManageableEvents.and.returnValue(pagedEnvelope([]));
    });

    it('resets to the first page when the lifecycle filter changes', () => {
      component.page = 4;

      component.setLifecycle('Paused');

      expect(component.selectedLifecycle).toBe('Paused');
      expect(component.page).toBe(1);
      expect(management.getManageableEvents.calls.mostRecent().args[1].lifecycleState).toBe(
        'Paused',
      );
    });

    it('clears the filter when asked for everything', () => {
      component.setLifecycle(null);

      expect(component.selectedLifecycle).toBeNull();
      expect(management.getManageableEvents.calls.mostRecent().args[1].lifecycleState).toBeNull();
    });

    it('computes at least one page even with nothing to show', () => {
      component.totalCount = 0;
      expect(component.totalPages).toBe(1);

      component.totalCount = 19;
      expect(component.totalPages).toBe(3);
    });

    it('moves between valid pages', () => {
      component.totalCount = 27;
      component.page = 1;

      component.goToPage(2);

      expect(component.page).toBe(2);
      expect(management.getManageableEvents.calls.mostRecent().args[1].page).toBe(2);
    });

    it('ignores a page that is out of range or already showing', () => {
      component.totalCount = 27;
      component.page = 2;

      component.goToPage(0);
      component.goToPage(99);
      component.goToPage(2);

      expect(component.page).toBe(2);
      expect(management.getManageableEvents).not.toHaveBeenCalled();
    });

    it('records the search term for the next load', () => {
      component.onEventSearch('robotics');

      expect(component.eventSearch).toBe('robotics');
    });

    it('maps every lifecycle state to a badge', () => {
      for (const state of ALL_LIFECYCLE_STATES) {
        expect(component.lifecycleBadge(state)).toContain('border');
      }
    });
  });

  describe('running a lifecycle action from a card', () => {
    it('swaps the card in place when no filter is active', () => {
      component.selectedLifecycle = null;
      component.events = [managedEvent({ id: 1 }), managedEvent({ id: 2 })];
      transitionReturns('Paused');

      component.runLifecycleTransition(component.events[0], 'pause');

      expect(component.events[0].lifecycleState).toBe('Paused');
      expect(component.events[1].lifecycleState).toBe('Published');
      expect(management.getManageableEvents).not.toHaveBeenCalled();
    });

    it('swaps in place when the event still matches the active filter', () => {
      component.selectedLifecycle = 'Published';
      component.events = [managedEvent({ id: 1, lifecycleState: 'Paused' })];
      transitionReturns('Published');

      component.runLifecycleTransition(component.events[0], 'resume');

      expect(component.events[0].lifecycleState).toBe('Published');
      expect(management.getManageableEvents).not.toHaveBeenCalled();
    });

    it('reloads when the event moves out of the active filter', () => {
      // Leaving the card in place would show a Paused card among the Published results, with a
      // total that no longer adds up.
      component.selectedLifecycle = 'Published';
      component.events = [managedEvent({ id: 1, lifecycleState: 'Published' })];
      transitionReturns('Paused');

      component.runLifecycleTransition(component.events[0], 'pause');

      expect(management.getManageableEvents).toHaveBeenCalledTimes(1);
      expect(management.getManageableEvents.calls.mostRecent().args[1].lifecycleState).toBe(
        'Published',
      );
    });

    it('clears the busy marker and reports a rejected transition', () => {
      component.selectedLifecycle = null;
      component.events = [managedEvent({ id: 1 })];
      // A real HttpErrorResponse, since getApiClientMessage only unwraps that shape.
      management.runTransition.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { message: 'Not ready to publish.' },
            }),
        ) as never,
      );

      component.runLifecycleTransition(component.events[0], 'publish');

      expect(component.error).toBe('Not ready to publish.');
      expect(component.transitioningEventId).toBeNull();
      expect(component.events[0].lifecycleState).toBe('Published', 'the card is left alone');
    });
  });
});
