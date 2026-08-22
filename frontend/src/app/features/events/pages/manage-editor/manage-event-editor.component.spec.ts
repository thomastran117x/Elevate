import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ManageEventEditorComponent } from './manage-event-editor.component';
import { EventsManagementService } from '../../services/events-management.service';
import { EventSeriesService } from '../../services/event-series.service';
import { ManagedEvent } from '../../models/event.types';

class ActivatedRouteStub {
  readonly parent: { snapshot: { paramMap: ReturnType<typeof convertToParamMap> } } | null;

  constructor(
    private readonly params: Record<string, string>,
    parentParams: Record<string, string> | null = { clubId: '4' },
  ) {
    this.parent = parentParams ? { snapshot: { paramMap: convertToParamMap(parentParams) } } : null;
  }

  get snapshot() {
    return { paramMap: convertToParamMap(this.params) };
  }
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
    availableTransitions: [],
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

  function setup(
    params: Record<string, string>,
    event?: ManagedEvent,
    parentParams: Record<string, string> | null = { clubId: '4' },
  ) {
    TestBed.resetTestingModule();

    managementService = jasmine.createSpyObj<EventsManagementService>('EventsManagementService', [
      'getManageableEvent',
      'createDraft',
      'updateDraft',
      'publishEvent',
      'cancelEvent',
      'archiveEvent',
      'runTransition',
      'revertLifecycle',
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
        { provide: ActivatedRoute, useValue: new ActivatedRouteStub(params, parentParams) },
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

    it('propagates the schedule step to later occurrences as a wall clock and a length', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));

      component.form.patchValue({
        startTime: '2026-06-08T18:30',
        endTime: '2026-06-08T20:00',
      });

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      const [, payload] = seriesService.updateFutureOccurrences.calls.mostRecent().args;

      // A wall-clock time of day, never an instant — the backend re-anchors it per occurrence
      // in the series' own zone.
      expect(payload.localStartTime).toBe('18:30');
      expect(payload.durationMinutes).toBe(90);
    });

    it('omits schedule fields when the end time is missing or invalid', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));

      component.form.patchValue({ startTime: '2026-06-08T18:30', endTime: '' });

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      const [, payload] = seriesService.updateFutureOccurrences.calls.mostRecent().args;

      expect(payload.localStartTime).toBe('18:30');
      expect(payload.durationMinutes).toBeUndefined();
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

  describe('recurrence preview resilience', () => {
    it('keeps previewing after a rejected rule', fakeAsync(() => {
      setup({ clubId: '4' });

      const goodPreview = {
        timeZoneId: 'America/New_York',
        occurrenceCount: 3,
        occurrences: [],
        warnings: [],
      };

      // First rule is rejected by the API, the next one is fine.
      seriesService.previewSeries.and.returnValues(
        throwError(() => ({ error: { message: 'Unknown time zone.' } })) as never,
        of(envelope(goodPreview)) as never,
      );

      component.recurrenceForm.patchValue({ enabled: true, startLocalDate: '2026-03-03' });
      tick(400);

      expect(component.previewError).toBe('Unknown time zone.');
      expect(component.preview).toBeNull();

      // Without catchError inside switchMap the subscription would be dead by now and this
      // second edit would never reach the service.
      component.recurrenceForm.patchValue({ occurrenceCount: 4 });
      tick(400);

      expect(seriesService.previewSeries).toHaveBeenCalledTimes(2);
      expect(component.previewError).toBe('');
      expect(component.preview).toEqual(goodPreview as never);
    }));

    it('clears a stale error once a later rule succeeds', fakeAsync(() => {
      setup({ clubId: '4' });

      seriesService.previewSeries.and.returnValues(
        throwError(() => ({ error: { Message: 'Too many occurrences.' } })) as never,
        of(
          envelope({
            timeZoneId: 'UTC',
            occurrenceCount: 1,
            occurrences: [],
            warnings: [],
          }),
        ) as never,
      );

      component.recurrenceForm.patchValue({ enabled: true, startLocalDate: '2026-03-03' });
      tick(400);
      expect(component.previewError).toBe('Too many occurrences.');

      component.recurrenceForm.patchValue({ occurrenceCount: 1 });
      tick(400);
      expect(component.previewError).toBe('');
      expect(component.previewing).toBeFalse();
    }));
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

    it('is not offered once the event has left Draft', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ lifecycleState: 'Published' }));
      component.recurrenceForm.controls.enabled.setValue(true);

      expect(component.canCreateSeries).toBeFalse();
    });

    it('is not offered while repeat is switched off', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      expect(component.isRecurring).toBeFalse();
      expect(component.canCreateSeries).toBeFalse();
    });
  });

  describe('route resolution', () => {
    it('falls back to the parent route for the club id', () => {
      setup({});

      expect(component.clubId).toBe(4);
      expect(component.eventId).toBe(0);
      expect(component.loading).toBeFalse();
    });

    it('loads the existing event when an id is present', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      expect(managementService.getManageableEvent).toHaveBeenCalledOnceWith(12);
      expect(component.event?.id).toBe(12);
      expect(component.loading).toBeFalse();
    });

    it('refuses to start a draft without a club', () => {
      setup({}, undefined, null);

      expect(component.clubId).toBe(0);
      expect(component.error).toBe('A valid club ID is required to create a draft.');
      expect(component.loading).toBeFalse();
    });

    it('rejects a non-numeric event id as no event at all', () => {
      setup({ clubId: '4', eventId: 'abc' });

      expect(component.eventId).toBe(0);
      expect(managementService.getManageableEvent).not.toHaveBeenCalled();
    });

    it('reports a failed load', () => {
      setup({ clubId: '4', eventId: '12' });
      managementService.getManageableEvent.and.returnValue(
        throwError(() => ({ error: { message: 'That event is gone.' } })) as never,
      );

      component.ngOnInit();

      expect(component.error).toBe('That event is gone.');
      expect(component.loading).toBeFalse();
    });

    it('falls back to a generic load message', () => {
      setup({ clubId: '4', eventId: '12' });
      managementService.getManageableEvent.and.returnValue(throwError(() => ({})) as never);

      component.ngOnInit();

      expect(component.error).toBe('We could not load this event.');
    });
  });

  describe('wizard navigation', () => {
    beforeEach(() => setup({ clubId: '4' }));

    it('reports progress as a percentage of the step count', () => {
      expect(component.progressPercent).toBe(0);

      component.goToStep(3);
      expect(component.progressPercent).toBeCloseTo((3 / 6) * 100);
    });

    it('refuses to jump outside the step range', () => {
      component.goToStep(-1);
      expect(component.currentStep).toBe(0);

      component.goToStep(component.steps.length);
      expect(component.currentStep).toBe(0);
    });

    it('walks forward and back, stopping at each end', () => {
      component.prevStep();
      expect(component.currentStep).toBe(0);

      component.nextStep();
      expect(component.currentStep).toBe(1);

      component.prevStep();
      expect(component.currentStep).toBe(0);

      component.goToStep(component.steps.length - 1);
      component.nextStep();
      expect(component.currentStep).toBe(component.steps.length - 1);
    });

    it('splits the review tags on commas, dropping blanks', () => {
      component.form.patchValue({ tags: ' board games , , social ' });

      expect(component.reviewTags).toEqual(['board games', 'social']);
    });

    it('yields no review tags for an empty field', () => {
      component.form.patchValue({ tags: '' });

      expect(component.reviewTags).toEqual([]);
    });
  });

  describe('lifecycle state helpers', () => {
    it('defaults to Draft with no loaded event', () => {
      setup({ clubId: '4' });

      expect(component.lifecycleState).toBe('Draft');
      expect(component.publishIssues).toEqual([]);
      expect(component.canManageInvitations).toBeFalse();
      expect(component.canManageWaitlist).toBeFalse();
    });

    it('surfaces the publish blockers reported by the API', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ publishIssues: ['Add a start time.'] }));

      expect(component.publishIssues).toEqual(['Add a start time.']);
    });

    it('offers invitations only for a published private event', () => {
      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ lifecycleState: 'Published', isPrivate: true }),
      );
      expect(component.canManageInvitations).toBeTrue();

      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ lifecycleState: 'Draft', isPrivate: true }),
      );
      expect(component.canManageInvitations).toBeFalse();

      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ lifecycleState: 'Published', isPrivate: false }),
      );
      expect(component.canManageInvitations).toBeFalse();
    });

    it('offers the waitlist only for a published waitlisted event', () => {
      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ lifecycleState: 'Published', waitlistEnabled: true }),
      );
      expect(component.canManageWaitlist).toBeTrue();

      setup({ clubId: '4', eventId: '12' }, buildEvent({ lifecycleState: 'Published' }));
      expect(component.canManageWaitlist).toBeFalse();
    });

    const badges: Array<ManagedEvent['lifecycleState']> = [
      'Draft',
      'Published',
      'Cancelled',
      'Archived',
    ];

    it('gives each lifecycle state its own badge', () => {
      setup({ clubId: '4' });

      const classes = badges.map((state) => component.lifecycleBadge(state));
      expect(new Set(classes).size).toBe(badges.length);
      for (const value of classes) {
        expect(value).toContain('border');
      }
    });
  });

  describe('waitlist availability', () => {
    beforeEach(() => setup({ clubId: '4' }));

    it('requires a capacity limit', () => {
      component.form.patchValue({ maxParticipants: null });

      expect(component.waitlistUnavailableReason).toBe('Waitlists require a capacity limit.');
    });

    it('rejects a zero capacity as well', () => {
      component.form.patchValue({ maxParticipants: 0 });

      expect(component.waitlistUnavailableReason).toBe('Waitlists require a capacity limit.');
    });

    it('rejects paid events', () => {
      component.form.patchValue({ maxParticipants: 20, registerCost: 15 });

      expect(component.waitlistUnavailableReason).toBe(
        "Waitlists aren't available for paid events.",
      );
    });

    it('allows a free event with a capacity', () => {
      component.form.patchValue({ maxParticipants: 20, registerCost: 0 });

      expect(component.waitlistUnavailableReason).toBeNull();
    });
  });

  describe('image uploads', () => {
    function fileInput(files: File[]): Event {
      const input = document.createElement('input');
      Object.defineProperty(input, 'files', { value: files, configurable: true });
      return { target: input } as unknown as Event;
    }

    const file = (name: string) => new File(['bytes'], name, { type: 'image/png' });

    it('appends each uploaded URL and clears the input', async () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ imageUrls: [] }));
      managementService.uploadImage.and.returnValues(
        of('https://cdn/a.png'),
        of('https://cdn/b.png'),
      );
      const event = fileInput([file('a.png'), file('b.png')]);

      await component.onFilesSelected(event);

      expect(component.imageUrls).toEqual(['https://cdn/a.png', 'https://cdn/b.png']);
      expect(component.uploading).toBeFalse();
      expect((event.target as HTMLInputElement).value).toBe('');
    });

    it('does nothing when no file was picked', async () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      await component.onFilesSelected(fileInput([]));

      expect(managementService.uploadImage).not.toHaveBeenCalled();
      expect(component.uploading).toBeFalse();
    });

    it('refuses to upload before the draft has a club', async () => {
      setup({});
      component.clubId = 0;

      await component.onFilesSelected(fileInput([file('a.png')]));

      expect(managementService.uploadImage).not.toHaveBeenCalled();
      expect(component.error).toBe('Save the draft first so we know which club owns these images.');
    });

    it('caps the gallery at five images', async () => {
      const existing = ['1', '2', '3', '4', '5'].map((n) => `https://cdn/${n}.png`);
      setup({ clubId: '4', eventId: '12' }, buildEvent({ imageUrls: existing }));
      managementService.uploadImage.and.returnValue(of('https://cdn/6.png'));

      await component.onFilesSelected(fileInput([file('6.png')]));

      expect(component.imageUrls).toEqual(existing);
    });

    it('reports an upload failure and still stops the spinner', async () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ imageUrls: [] }));
      managementService.uploadImage.and.returnValue(
        throwError(() => new Error('The image upload failed.')),
      );

      await component.onFilesSelected(fileInput([file('a.png')]));

      expect(component.error).toBe('The image upload failed.');
      expect(component.uploading).toBeFalse();
    });

    it('falls back to a generic message for a non-Error rejection', async () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ imageUrls: [] }));
      managementService.uploadImage.and.returnValue(throwError(() => 'nope'));

      await component.onFilesSelected(fileInput([file('a.png')]));

      expect(component.error).toBe('We could not upload one or more images.');
    });

    it('removes an image by index', () => {
      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ imageUrls: ['https://cdn/a.png', 'https://cdn/b.png'] }),
      );

      component.removeImage(0);

      expect(component.imageUrls).toEqual(['https://cdn/b.png']);
    });
  });

  describe('persisting a draft', () => {
    it('creates a new draft and rewrites the URL to the saved event', () => {
      setup({ clubId: '4' });
      managementService.createDraft.and.returnValue(of(envelope(buildEvent({ id: 99 }))) as never);
      const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;

      component.form.patchValue({ name: 'New event' });
      component.saveDraft();

      expect(managementService.createDraft).toHaveBeenCalled();
      expect(component.eventId).toBe(99);
      expect(component.successMessage).toContain('Draft created');
      expect(router.navigate).toHaveBeenCalledWith(['/clubs', 4, 'manage', 'events', 99], {
        replaceUrl: true,
      });
      expect(component.saving).toBeFalse();
    });

    it('updates an existing draft without navigating', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;

      component.saveDraft();

      expect(managementService.updateDraft).toHaveBeenCalled();
      expect(component.successMessage).toBe('Draft saved.');
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('normalizes the payload it sends', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      component.form.patchValue({
        name: '  Trimmed  ',
        description: '   ',
        maxParticipants: null,
        registerCost: null,
        startTime: '2026-06-08T18:30',
        endTime: '',
        tags: ' a , , b ',
      });

      component.saveDraft();

      const [, payload] = managementService.updateDraft.calls.mostRecent().args;
      expect(payload.name).toBe('Trimmed');
      expect(payload.description).toBeUndefined();
      expect(payload.maxParticipants).toBeUndefined();
      expect(payload.registerCost).toBe(0);
      expect(payload.endTime).toBeNull();
      expect(payload.tags).toEqual(['a', 'b']);
    });

    it('drops an unparseable start time rather than sending Invalid Date', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      component.form.patchValue({ startTime: 'not-a-date', tags: '' });
      component.saveDraft();

      const [, payload] = managementService.updateDraft.calls.mostRecent().args;
      expect(payload.startTime).toBeUndefined();
      expect(payload.tags).toBeUndefined();
    });

    it('reports a save failure', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      managementService.updateDraft.and.returnValue(
        throwError(() => ({ error: { Message: 'Name is required.' } })) as never,
      );

      component.saveDraft();

      expect(component.error).toBe('Name is required.');
      expect(component.saving).toBeFalse();
    });

    it('falls back to a generic save message', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      managementService.updateDraft.and.returnValue(throwError(() => ({})) as never);

      component.saveDraft();

      expect(component.error).toBe('We could not save the draft.');
    });
  });

  describe('lifecycle actions', () => {
    it('refuses to change state on an unsaved draft', () => {
      setup({ clubId: '4' });

      component.runLifecycleTransition('publish');

      expect(managementService.runTransition).not.toHaveBeenCalled();
      expect(component.error).toBe('Save the draft before changing its state.');
    });

    for (const [key, state] of [
      ['publish', 'Published'],
      ['pause', 'Paused'],
      ['cancel', 'Cancelled'],
      ['archive', 'Archived'],
      ['reinstate', 'Published'],
      ['unarchive', 'Paused'],
    ] as const) {
      it(`runs '${key}' by key and applies the state the server returns`, () => {
        setup({ clubId: '4', eventId: '12' }, buildEvent());
        managementService.runTransition.and.returnValue(
          of(envelope(buildEvent({ lifecycleState: state }))) as never,
        );

        component.runLifecycleTransition(key);

        expect(managementService.runTransition).toHaveBeenCalledOnceWith(12, key);
        expect(component.lifecycleState).toBe(state);
        expect(component.successMessage).toContain(state.toLowerCase());
        expect(component.saving).toBeFalse();
      });
    }

    it('reports a rejected lifecycle transition', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      managementService.runTransition.and.returnValue(
        throwError(() => ({ error: { message: 'Add a start time first.' } })) as never,
      );

      component.runLifecycleTransition('publish');

      expect(component.error).toBe('Add a start time first.');
      expect(component.saving).toBeFalse();
    });

    it('falls back to a generic lifecycle message', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      managementService.runTransition.and.returnValue(throwError(() => ({})) as never);

      component.runLifecycleTransition('archive');

      expect(component.error).toBe('The lifecycle action could not be completed.');
    });
  });

  describe('undoing a lifecycle change', () => {
    it('offers undo only while the server still says the window is open', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      expect(component.canUndoLifecycleChange).toBeFalse();

      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ revertAvailableUntil: '2026-08-23T12:00:00Z' }),
      );
      expect(component.canUndoLifecycleChange).toBeTrue();
    });

    it('reverts through the server and applies the restored state', () => {
      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ lifecycleState: 'Cancelled', revertAvailableUntil: '2026-08-23T12:00:00Z' }),
      );
      managementService.revertLifecycle.and.returnValue(
        of(envelope(buildEvent({ lifecycleState: 'Published' }))) as never,
      );

      component.undoLifecycleChange();

      expect(managementService.revertLifecycle).toHaveBeenCalledOnceWith(12);
      expect(component.lifecycleState).toBe('Published');

      // The server clears the window once undo is spent, so the button goes away.
      expect(component.canUndoLifecycleChange).toBeFalse();
    });

    it('surfaces a lapsed window as an error rather than failing silently', () => {
      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ revertAvailableUntil: '2026-08-23T12:00:00Z' }),
      );
      managementService.revertLifecycle.and.returnValue(
        throwError(() => ({
          error: { message: 'The window for undoing this change has passed.' },
        })) as never,
      );

      component.undoLifecycleChange();

      expect(component.error).toBe('The window for undoing this change has passed.');
    });
  });

  describe('creating a series', () => {
    it('refuses without a repeat start date', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      component.createSeries();

      expect(seriesService.createSeries).not.toHaveBeenCalled();
      expect(component.error).toBe('Add a repeat start date before creating the series.');
    });

    it('creates the series and routes to it', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      seriesService.createSeries.and.returnValue(
        of(envelope({ id: 7, occurrences: [{}, {}, {}] })) as never,
      );
      const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;

      component.recurrenceForm.patchValue({ enabled: true, startLocalDate: '2026-03-03' });
      component.createSeries();

      expect(seriesService.createSeries).toHaveBeenCalled();
      expect(component.successMessage).toContain('Created 3 occurrences');
      expect(router.navigate).toHaveBeenCalledWith(['/clubs', 4, 'manage', 'series', 7]);
      expect(component.saving).toBeFalse();
    });

    it('reports a rejected series', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());
      seriesService.createSeries.and.returnValue(
        throwError(() => ({ error: { message: 'That rule produces no dates.' } })) as never,
      );

      component.recurrenceForm.patchValue({ enabled: true, startLocalDate: '2026-03-03' });
      component.createSeries();

      expect(component.error).toBe('That rule produces no dates.');
      expect(component.saving).toBeFalse();
    });
  });

  describe('recurrence rule assembly', () => {
    beforeEach(() => setup({ clubId: '4' }));

    it('sends weekdays only for a weekly rule', fakeAsync(() => {
      component.recurrenceForm.patchValue({
        enabled: true,
        startLocalDate: '2026-03-03',
        frequency: 'Weekly',
        byWeekdays: [1, 3],
      });
      tick(400);

      const [, weekly] = seriesService.previewSeries.calls.mostRecent().args;
      expect(weekly.byWeekdays).toEqual([1, 3]);

      component.recurrenceForm.patchValue({ frequency: 'Monthly' });
      tick(400);

      const [, monthly] = seriesService.previewSeries.calls.mostRecent().args;
      expect(monthly.byWeekdays).toBeUndefined();
    }));

    it('sends an end date only in UntilDate mode, and a count only in Count mode', fakeAsync(() => {
      component.recurrenceForm.patchValue({
        enabled: true,
        startLocalDate: '2026-03-03',
        endMode: 'UntilDate',
        endLocalDate: '2026-06-01',
        occurrenceCount: 12,
      });
      tick(400);

      const [, untilDate] = seriesService.previewSeries.calls.mostRecent().args;
      expect(untilDate.endLocalDate).toBe('2026-06-01');
      expect(untilDate.occurrenceCount).toBeNull();

      component.recurrenceForm.patchValue({ endMode: 'Count' });
      tick(400);

      const [, byCount] = seriesService.previewSeries.calls.mostRecent().args;
      expect(byCount.endLocalDate).toBeNull();
      expect(byCount.occurrenceCount).toBe(12);
      expect(component.endsByCount).toBeTrue();
    }));

    it('never previews while repeat is switched off', fakeAsync(() => {
      component.recurrenceForm.patchValue({ startLocalDate: '2026-03-03' });
      tick(400);

      expect(seriesService.previewSeries).not.toHaveBeenCalled();
      expect(component.preview).toBeNull();
      expect(component.previewError).toBe('');
    }));

    it('never previews without a start date', fakeAsync(() => {
      component.recurrenceForm.patchValue({ enabled: true, startLocalDate: '' });
      tick(400);

      expect(seriesService.previewSeries).not.toHaveBeenCalled();
    }));

    it('falls back to a generic message for a preview error with no body', fakeAsync(() => {
      seriesService.previewSeries.and.returnValue(throwError(() => ({})) as never);

      component.recurrenceForm.patchValue({ enabled: true, startLocalDate: '2026-03-03' });
      tick(400);

      expect(component.previewError).toBe('We could not work out those repeat dates.');
    }));
  });

  describe('series-wide save', () => {
    it('reports how many occurrences changed, singular or plural', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));
      seriesService.updateFutureOccurrences.and.returnValue(
        of(
          envelope({
            seriesId: 3,
            affectedCount: 1,
            affectedEventIds: [12],
            skipped: [],
            retimedWithRegistrations: [],
          }),
        ) as never,
      );

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      expect(component.successMessage).toBe('Updated 1 occurrence.');
    });

    it('surfaces the skipped-occurrence notice', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));
      seriesService.describeSkipped.and.returnValue('1 occurrence was left alone.');

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      expect(component.seriesNotice).toBe('1 occurrence was left alone.');
    });

    it('reports a rejected series update', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));
      seriesService.updateFutureOccurrences.and.returnValue(
        throwError(() => ({ error: { Message: 'That change is not allowed.' } })) as never,
      );

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      expect(component.error).toBe('That change is not allowed.');
      expect(component.saving).toBeFalse();
    });

    it('falls back to a generic series message', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: 1 }));
      seriesService.updateFutureOccurrences.and.returnValue(throwError(() => ({})) as never);

      component.saveDraft();
      component.onScopeChosen('thisAndFollowing');

      expect(component.error).toBe('We could not update the later occurrences.');
    });

    it('labels an occurrence with no index generically', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ seriesId: 3, occurrenceIndex: null }));

      expect(component.occurrenceLabel).toBe('Part of a repeating series');
    });

    it('has no occurrence label for a standalone event', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent());

      expect(component.occurrenceLabel).toBe('');
      expect(component.belongsToSeries).toBeFalse();
    });
  });

  describe('seeding the repeat step', () => {
    it('pre-fills the repeat date and time from the event schedule', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ startTime: '2026-06-01T09:00:00Z' }));

      const raw = component.recurrenceForm.getRawValue();
      expect(raw.startLocalDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      expect(raw.startLocalTime).toMatch(/^\d{2}:\d{2}$/);
    });

    it('keeps the series time zone rather than the editor’s own', () => {
      setup(
        { clubId: '4', eventId: '12' },
        buildEvent({ timeZoneId: 'Australia/Sydney', seriesId: 3 }),
      );

      expect(component.recurrenceForm.getRawValue().timeZoneId).toBe('Australia/Sydney');
    });

    it('defaults the repeat time when the event has no schedule yet', () => {
      setup({ clubId: '4', eventId: '12' }, buildEvent({ startTime: undefined }));

      const raw = component.recurrenceForm.getRawValue();
      expect(raw.startLocalDate).toBe('');
      expect(raw.startLocalTime).toBe('19:00');
    });
  });
});
