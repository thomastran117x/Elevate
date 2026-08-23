import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ManagedEvent } from '../../../../events/models/event.types';
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
            snapshot: { paramMap: convertToParamMap({ clubId: '3' }) },
            parent: null,
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
