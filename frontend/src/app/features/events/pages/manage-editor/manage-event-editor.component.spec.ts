import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { ManageEventEditorComponent } from './manage-event-editor.component';
import { EventsManagementService } from '../../services/events-management.service';
import { EventSeriesService } from '../../services/event-series.service';
import { ManagedEvent } from '../../models/event.types';

class ActivatedRouteStub {
  constructor(private readonly params: Record<string, string>) {}

  get snapshot() {
    return { paramMap: convertToParamMap(this.params) };
  }

  readonly parent = {
    snapshot: { paramMap: convertToParamMap({ clubId: '4' }) },
  };
}

function buildEvent(overrides: Partial<ManagedEvent> = {}): ManagedEvent {
  return {
    id: 12,
    name: 'Weekly Tabletop',
    description: 'A recurring evening of board games.',
    location: 'Studio 1',
    imageUrls: ['https://cdn.test/a.png'],
    isPrivate: false,
    maxParticipants: 30,
    registerCost: 0,
    startTime: '2026-06-01T09:00:00Z',
    endTime: '2026-06-01T11:00:00Z',
    clubId: 4,
    currentVersionNumber: 1,
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
    lifecycleState: 'Draft',
    category: 'Other',
    tags: [],
    registrationCount: 0,
    waitlistEnabled: false,
    waitlistCount: 0,
    publishReady: true,
    publishIssues: [],
    ...overrides,
  };
}

describe('ManageEventEditorComponent', () => {
  let fixture: ComponentFixture<ManageEventEditorComponent>;
  let component: ManageEventEditorComponent;
  let managementService: jasmine.SpyObj<EventsManagementService>;
  let seriesService: jasmine.SpyObj<EventSeriesService>;

  const envelope = <T>(data: T) => ({
    success: true,
    message: 'ok',
    data,
    error: null,
    meta: null,
  });

  function setup(params: Record<string, string>, event?: ManagedEvent) {
    managementService = jasmine.createSpyObj<EventsManagementService>('EventsManagementService', [
      'getManageableEvent',
      'createDraft',
      'updateDraft',
      'publishEvent',
      'cancelEvent',
      'archiveEvent',
      'uploadImage',
    ]);
    seriesService = jasmine.createSpyObj<EventSeriesService>('EventSeriesService', [
      'previewSeries',
      'createSeries',
      'updateFutureOccurrences',
      'describeSkipped',
    ]);

    managementService.getManageableEvent.and.returnValue(
      of(envelope(event ?? buildEvent())) as never,
    );
    managementService.updateDraft.and.returnValue(of(envelope(event ?? buildEvent())) as never);
    seriesService.previewSeries.and.returnValue(
      of(
        envelope({
          timeZoneId: 'America/New_York',
          occurrenceCount: 0,
          occurrences: [],
          warnings: [],
        }),
      ) as never,
    );
    seriesService.updateFutureOccurrences.and.returnValue(
      of(
        envelope({
          seriesId: 3,
          affectedCount: 2,
          affectedEventIds: [12, 13],
          skipped: [],
          retimedWithRegistrations: [],
        }),
      ) as never,
    );
    seriesService.describeSkipped.and.returnValue(null);

    TestBed.configureTestingModule({
      imports: [ManageEventEditorComponent],
      providers: [
        { provide: ActivatedRoute, useValue: new ActivatedRouteStub(params) },
        { provide: EventsManagementService, useValue: managementService },
        { provide: EventSeriesService, useValue: seriesService },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
      ],
    });

    fixture = TestBed.createComponent(ManageEventEditorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  describe('wall-clock handling', () => {
    beforeEach(() => setup({ clubId: '4' }));

    it('builds the recurrence start without any time zone conversion', () => {
      // Zone-independent by construction: no Date is ever created, so this holds on any
      // machine regardless of the runner's TZ.
      expect(component.buildRecurrenceStart('2026-03-03', '19:00')).toBe('2026-03-03T19:00:00');
    });

    it('never emits a UTC marker or offset in the recurrence start', () => {
      const start = component.buildRecurrenceStart('2026-11-01', '01:30');

      expect(start.endsWith('Z')).toBeFalse();
      expect(start).not.toContain('+');
      expect(start.split('T')[1]).toBe('01:30:00');
    });

    it('defaults a blank time rather than producing an invalid string', () => {
      expect(component.buildRecurrenceStart('2026-03-03', '')).toBe('2026-03-03T00:00:00');
    });
  });

  describe('wizard steps', () => {
    beforeEach(() => setup({ clubId: '4' }));

    it('includes a Repeat step between Schedule and Location', () => {
      expect(component.steps.map((step) => step.label)).toEqual([
        'Basics',
        'Schedule',
        'Repeat',
        'Location',
        'Details',
        'Images',
        'Review',
      ]);
    });

    it('treats the last step as the review step', () => {
      component.goToStep(component.steps.length - 1);
      expect(component.isReviewStep).toBeTrue();
    });
  });

  describe('recurrence rule state', () => {
    beforeEach(() => setup({ clubId: '4' }));

    it('reports weekly and monthly modes from the frequency control', () => {
      component.recurrenceForm.controls.frequency.setValue('Weekly');
      expect(component.isWeekly).toBeTrue();
      expect(component.isMonthly).toBeFalse();

      component.recurrenceForm.controls.frequency.setValue('Monthly');
      expect(component.isMonthly).toBeTrue();
    });

    it('toggles weekdays on and off', () => {
      component.toggleWeekday(2);
      expect(component.isWeekdaySelected(2)).toBeTrue();

      component.toggleWeekday(2);
      expect(component.isWeekdaySelected(2)).toBeFalse();
    });
  });

  describe('saving an occurrence', () => {
    it('saves directly when the event is standalone', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      component.saveDraft();

      expect(component.scopeDialogOpen).toBeFalse();
      expect(managementService.updateDraft).toHaveBeenCalled();
    });

    it('asks for a scope first when the event belongs to a series', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));

      component.saveDraft();

      expect(component.scopeDialogOpen).toBeTrue();
      expect(managementService.updateDraft).not.toHaveBeenCalled();
    });

    it('updates only this occurrence when that scope is chosen', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));

      component.saveDraft();
      component.onScopeChosen('this');

      expect(managementService.updateDraft).toHaveBeenCalled();
      expect(seriesService.updateFutureOccurrences).not.toHaveBeenCalled();
    });

    it('patches the series when this-and-following is chosen', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      expect(seriesService.updateFutureOccurrences).toHaveBeenCalled();

      const [seriesId, payload] = seriesService.updateFutureOccurrences.calls.mostRecent().args;
      expect(seriesId).toBe(3);
      expect(payload.fromEventId).toBe(12);
      expect(managementService.updateDraft).not.toHaveBeenCalled();
    });

    it('saves nothing when the organizer dismisses the dialog', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));

      component.saveDraft();
      component.onScopeChosen(null);

      expect(component.scopeDialogOpen).toBeFalse();
      expect(managementService.updateDraft).not.toHaveBeenCalled();
      expect(seriesService.updateFutureOccurrences).not.toHaveBeenCalled();
    });

    it('labels the occurrence being edited', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 2 }));

      expect(component.occurrenceLabel).toContain('Occurrence 3');
    });
  });

  describe('series creation availability', () => {
    it('is offered for a saved standalone draft with repeat enabled', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      component.recurrenceForm.controls.enabled.setValue(true);

      expect(component.canCreateSeries).toBeTrue();
    });

    it('is not offered for an event that is already an occurrence', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 0 }));
      component.recurrenceForm.controls.enabled.setValue(true);

      expect(component.canCreateSeries).toBeFalse();
    });

    it('is not offered before the draft has been saved', () => {
      setup({ clubId: '4' });
      component.recurrenceForm.controls.enabled.setValue(true);

      expect(component.canCreateSeries).toBeFalse();
    });
  });
});
