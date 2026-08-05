import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, firstValueFrom, map, of, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { requireEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import {
  ALL_CATEGORIES,
  ALL_RECURRENCE_FREQUENCIES,
  EventDraftPayload,
  EventLifecycleState,
  ManagedEvent,
  OccurrenceEditScope,
  RecurrenceRule,
  SeriesPreview,
  UpdateFutureOccurrencesPayload,
} from '../../models/event.types';
import { detectBrowserTimeZone, formatLocalStart, listTimeZones } from '../../models/time-zones';
import { EventsManagementService } from '../../services/events-management.service';
import { EventSeriesService } from '../../services/event-series.service';
import { OccurrenceScopeDialogComponent } from '../../components/occurrence-scope-dialog/occurrence-scope-dialog.component';

@Component({
  selector: 'app-manage-event-editor',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, OccurrenceScopeDialogComponent],
  templateUrl: './manage-event-editor.component.html',
})
export class ManageEventEditorComponent {
  private readonly fb = new FormBuilder();
  private readonly seriesService = inject(EventSeriesService);

  readonly categories = ALL_CATEGORIES;
  readonly frequencies = ALL_RECURRENCE_FREQUENCIES;
  readonly timeZones = listTimeZones();
  readonly weekdays = [
    { value: 1, label: 'Mon' },
    { value: 2, label: 'Tue' },
    { value: 3, label: 'Wed' },
    { value: 4, label: 'Thu' },
    { value: 5, label: 'Fri' },
    { value: 6, label: 'Sat' },
    { value: 0, label: 'Sun' },
  ];
  readonly formatLocalStart = formatLocalStart;
  readonly inputClass =
    'w-full rounded-2xl border border-line bg-glass-strong px-4 py-3 text-base text-content placeholder:text-faint outline-none transition focus:border-accent focus:ring-4 focus:ring-accent/15';
  readonly form = this.fb.group({
    name: ['', [Validators.minLength(3), Validators.maxLength(30)]],
    description: ['', [Validators.maxLength(200)]],
    location: ['', [Validators.maxLength(50)]],
    isPrivate: [false],
    maxParticipants: [null as number | null],
    registerCost: [0],
    waitlistEnabled: [false],
    startTime: [''],
    endTime: [''],
    category: ['Other'],
    venueName: [''],
    city: [''],
    latitude: [null as number | null],
    longitude: [null as number | null],
    tags: [''],
  });

  /**
   * The repeat rule.
   *
   * Start date and time are kept as separate plain strings, and the zone is chosen explicitly,
   * so the wall clock the organizer types is sent verbatim. Running these through `toIso()` like
   * the Schedule step does would reinterpret them in the *browser's* zone — the precise bug the
   * time zone selector exists to prevent.
   */
  readonly recurrenceForm = this.fb.group({
    enabled: [false],
    frequency: ['Weekly'],
    interval: [1],
    byWeekdays: [[] as number[]],
    monthlyDayPolicy: ['ClampToMonthEnd'],
    timeZoneId: [detectBrowserTimeZone()],
    startLocalDate: [''],
    startLocalTime: ['19:00'],
    durationMinutes: [120 as number | null],
    endMode: ['Count'],
    endLocalDate: [''],
    occurrenceCount: [8 as number | null],
  });

  clubId = 0;
  eventId = 0;
  event: ManagedEvent | null = null;
  imageUrls: string[] = [];
  loading = true;
  saving = false;
  uploading = false;
  error = '';
  successMessage = '';

  // Wizard state. Repeat sits right after Schedule, while the timing context is fresh.
  // NOTE: the template switches on the *index*, so inserting a step here means renumbering
  // every @case in manage-event-editor.component.html.
  readonly steps = [
    { label: 'Basics', hint: 'Name, category & description' },
    { label: 'Schedule', hint: 'When it happens' },
    { label: 'Repeat', hint: 'Optional recurring series' },
    { label: 'Location', hint: 'Where it happens' },
    { label: 'Details', hint: 'Capacity, pricing & tags' },
    { label: 'Images', hint: 'Optional gallery' },
    { label: 'Review', hint: 'Confirm & submit' },
  ];
  currentStep = 0;

  // Recurrence state
  preview: SeriesPreview | null = null;
  previewError = '';
  previewing = false;
  scopeDialogOpen = false;
  seriesNotice = '';

  private readonly previewRequests = new Subject<RecurrenceRule>();
  private pendingScopeSave: (() => void) | null = null;

  get progressPercent(): number {
    return (this.currentStep / (this.steps.length - 1)) * 100;
  }

  get isReviewStep(): boolean {
    return this.currentStep === this.steps.length - 1;
  }

  get reviewTags(): string[] {
    return (this.form.controls.tags.value ?? '')
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean);
  }

  goToStep(index: number): void {
    if (index < 0 || index >= this.steps.length) return;
    this.currentStep = index;
  }

  nextStep(): void {
    if (this.currentStep < this.steps.length - 1) this.currentStep += 1;
  }

  prevStep(): void {
    if (this.currentStep > 0) this.currentStep -= 1;
  }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private managementService: EventsManagementService,
  ) {
    // Debounced so dragging the interval spinner does not fire a request per tick.
    this.previewRequests
      .pipe(
        debounceTime(400),
        // catchError sits INSIDE switchMap on purpose. An error escaping to the outer stream
        // would complete this subscription for good, so one invalid rule — a bad time zone,
        // too many occurrences — would silently kill the preview until the page was reloaded.
        // Handling it here turns a rejected rule into an ordinary emission and keeps the
        // stream listening for the next edit.
        switchMap((rule) =>
          this.seriesService.previewSeries(this.clubId, rule).pipe(
            map((response) => ({ preview: response.data ?? null, error: '' })),
            catchError((error: unknown) => {
              const payload = error as { error?: { message?: string; Message?: string } };

              return of({
                preview: null,
                error:
                  payload?.error?.message ||
                  payload?.error?.Message ||
                  'We could not work out those repeat dates.',
              });
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ preview, error }) => {
        this.preview = preview;
        this.previewError = error;
        this.previewing = false;
      });

    this.recurrenceForm.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.requestPreview();
    });
  }

  get isRecurring(): boolean {
    return !!this.recurrenceForm.controls.enabled.value;
  }

  get isWeekly(): boolean {
    return this.recurrenceForm.controls.frequency.value === 'Weekly';
  }

  get isMonthly(): boolean {
    return this.recurrenceForm.controls.frequency.value === 'Monthly';
  }

  get endsByCount(): boolean {
    return this.recurrenceForm.controls.endMode.value === 'Count';
  }

  /** True once this event is an occurrence, which is what triggers the scope prompt on save. */
  get belongsToSeries(): boolean {
    return !!this.event?.seriesId;
  }

  get occurrenceLabel(): string {
    if (!this.event?.seriesId) {
      return '';
    }

    const index = this.event.occurrenceIndex;
    return index === null || index === undefined
      ? 'Part of a repeating series'
      : `Occurrence ${index + 1} of a repeating series`;
  }

  get canCreateSeries(): boolean {
    return (
      this.isRecurring && !!this.eventId && !this.belongsToSeries && this.lifecycleState === 'Draft'
    );
  }

  toggleWeekday(value: number): void {
    const current = this.recurrenceForm.controls.byWeekdays.value ?? [];
    const next = current.includes(value)
      ? current.filter((day) => day !== value)
      : [...current, value];

    this.recurrenceForm.controls.byWeekdays.setValue(next);
  }

  isWeekdaySelected(value: number): boolean {
    return (this.recurrenceForm.controls.byWeekdays.value ?? []).includes(value);
  }

  /**
   * Assembles the wall-clock start by string concatenation. Deliberately never constructs a
   * `Date`, so the value cannot be shifted into the browser's zone on the way out.
   */
  buildRecurrenceStart(date: string, time: string): string {
    return `${date}T${(time || '00:00').slice(0, 5)}:00`;
  }

  createSeries(): void {
    const rule = this.buildRecurrenceRule();

    if (!rule || !this.eventId) {
      this.error = 'Add a repeat start date before creating the series.';
      return;
    }

    this.saving = true;
    this.error = '';
    this.successMessage = '';

    this.seriesService.createSeries(this.eventId, rule).subscribe({
      next: (response) => {
        const series = requireEnvelopeData(response, 'The series could not be created.');
        this.saving = false;
        this.successMessage = `Created ${series.occurrences.length} occurrences. They are drafts until you publish the series.`;

        void this.router.navigate(['/clubs', this.clubId, 'manage', 'series', series.id]);
      },
      error: (error) => {
        this.saving = false;
        this.error =
          error?.error?.message || error?.error?.Message || 'We could not create the series.';
      },
    });
  }

  onScopeChosen(scope: OccurrenceEditScope | null): void {
    this.scopeDialogOpen = false;

    if (!scope) {
      this.pendingScopeSave = null;
      return;
    }

    if (scope === 'this') {
      const save = this.pendingScopeSave;
      this.pendingScopeSave = null;
      save?.();
      return;
    }

    this.pendingScopeSave = null;
    this.saveAcrossFutureOccurrences();
  }

  private requestPreview(): void {
    const rule = this.buildRecurrenceRule();

    if (!rule || !this.clubId) {
      this.preview = null;
      this.previewError = '';
      return;
    }

    this.previewing = true;
    this.previewRequests.next(rule);
  }

  private buildRecurrenceRule(): RecurrenceRule | null {
    const raw = this.recurrenceForm.getRawValue();

    if (!raw.enabled || !raw.startLocalDate) {
      return null;
    }

    return {
      frequency: raw.frequency as RecurrenceRule['frequency'],
      interval: raw.interval ?? 1,
      byWeekdays: raw.frequency === 'Weekly' ? (raw.byWeekdays ?? []) : undefined,
      monthlyDayPolicy: raw.monthlyDayPolicy as RecurrenceRule['monthlyDayPolicy'],
      startLocalDateTime: this.buildRecurrenceStart(raw.startLocalDate, raw.startLocalTime ?? ''),
      durationMinutes: raw.durationMinutes ?? null,
      timeZoneId: raw.timeZoneId ?? detectBrowserTimeZone(),
      endMode: raw.endMode as RecurrenceRule['endMode'],
      endLocalDate: raw.endMode === 'UntilDate' ? (raw.endLocalDate ?? null) : null,
      occurrenceCount: raw.endMode === 'Count' ? (raw.occurrenceCount ?? null) : null,
    };
  }

  /** Pushes the current form values onto this and every later occurrence. */
  private saveAcrossFutureOccurrences(): void {
    const seriesId = this.event?.seriesId;

    if (!seriesId || !this.eventId) {
      return;
    }

    this.saving = true;
    this.error = '';
    this.successMessage = '';
    this.seriesNotice = '';

    const raw = this.form.getRawValue();
    const payload: UpdateFutureOccurrencesPayload = {
      fromEventId: this.eventId,
      name: this.optionalText(raw.name),
      description: this.optionalText(raw.description),
      location: this.optionalText(raw.location),
      venueName: this.optionalText(raw.venueName),
      city: this.optionalText(raw.city),
      latitude: raw.latitude,
      longitude: raw.longitude,
      maxParticipants: raw.maxParticipants ?? undefined,
      registerCost: raw.registerCost ?? undefined,
      isPrivate: !!raw.isPrivate,
      waitlistEnabled: !!raw.waitlistEnabled,
      category: raw.category as UpdateFutureOccurrencesPayload['category'],
      tags: this.parseTags(raw.tags),
      imageUrls: this.imageUrls,
      // The Schedule step has to travel with the rest, or changing the start time appears to
      // save while every later occurrence quietly keeps its old slot. Sent as a wall-clock
      // time of day plus a length, so the backend can re-anchor each occurrence in the
      // series' own zone rather than shifting them all by a fixed UTC offset.
      localStartTime: this.toLocalTimeOfDay(raw.startTime),
      durationMinutes: this.toDurationMinutes(raw.startTime, raw.endTime),
    };

    this.seriesService.updateFutureOccurrences(seriesId, payload).subscribe({
      next: (response) => {
        const result = requireEnvelopeData(response, 'The series could not be updated.');
        this.saving = false;
        this.successMessage = `Updated ${result.affectedCount} ${
          result.affectedCount === 1 ? 'occurrence' : 'occurrences'
        }.`;
        this.seriesNotice = this.seriesService.describeSkipped(result) ?? '';
        this.loadExisting();
      },
      error: (error) => {
        this.saving = false;
        this.error =
          error?.error?.message ||
          error?.error?.Message ||
          'We could not update the later occurrences.';
      },
    });
  }

  ngOnInit(): void {
    // clubId lives on the parent (club shell) route when nested under /clubs/:clubId/manage/events.
    const clubId = Number.parseInt(
      this.route.snapshot.paramMap.get('clubId') ??
        this.route.parent?.snapshot.paramMap.get('clubId') ??
        '',
      10,
    );
    const eventId = Number.parseInt(this.route.snapshot.paramMap.get('eventId') ?? '', 10);

    this.clubId = Number.isFinite(clubId) && clubId > 0 ? clubId : 0;
    this.eventId = Number.isFinite(eventId) && eventId > 0 ? eventId : 0;

    if (this.eventId) {
      this.loadExisting();
      return;
    }

    if (!this.clubId) {
      this.loading = false;
      this.error = 'A valid club ID is required to create a draft.';
      return;
    }

    this.loading = false;
  }

  get lifecycleState(): EventLifecycleState {
    return this.event?.lifecycleState ?? 'Draft';
  }

  get publishIssues(): string[] {
    return this.event?.publishIssues ?? [];
  }

  get canManageInvitations(): boolean {
    return !!this.event && this.event.lifecycleState === 'Published' && this.event.isPrivate;
  }

  get canManageWaitlist(): boolean {
    return !!this.event && this.event.lifecycleState === 'Published' && this.event.waitlistEnabled;
  }

  /**
   * Why the waitlist toggle is unavailable, or null when it can be enabled. A waitlist needs
   * a capacity to be full against, and paid events enforce capacity at checkout instead.
   */
  get waitlistUnavailableReason(): string | null {
    const raw = this.form.getRawValue();

    if (!raw.maxParticipants || raw.maxParticipants <= 0) {
      return 'Waitlists require a capacity limit.';
    }

    if ((raw.registerCost ?? 0) > 0) {
      return "Waitlists aren't available for paid events.";
    }

    return null;
  }

  async onFilesSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement | null;
    const files = Array.from(input?.files ?? []);

    if (!files.length) {
      return;
    }

    const targetClubId = this.event?.clubId || this.clubId;
    if (!targetClubId) {
      this.error = 'Save the draft first so we know which club owns these images.';
      return;
    }

    this.uploading = true;
    this.error = '';

    try {
      for (const file of files) {
        const publicUrl = await firstValueFrom(
          this.managementService.uploadImage(targetClubId, file, this.event?.id),
        );

        if (this.imageUrls.length < 5) {
          this.imageUrls = [...this.imageUrls, publicUrl];
        }
      }
    } catch (error: unknown) {
      this.error =
        error instanceof Error ? error.message : 'We could not upload one or more images.';
    } finally {
      this.uploading = false;
      if (input) {
        input.value = '';
      }
    }
  }

  removeImage(index: number): void {
    this.imageUrls = this.imageUrls.filter((_, currentIndex) => currentIndex !== index);
  }

  saveDraft(): void {
    // An occurrence belongs to a series, so saving it is ambiguous until the organizer says
    // whether they mean just this date or every later one too.
    if (this.belongsToSeries && !this.scopeDialogOpen) {
      this.pendingScopeSave = () => this.persistDraft();
      this.scopeDialogOpen = true;
      return;
    }

    this.persistDraft();
  }

  private persistDraft(): void {
    this.saving = true;
    this.error = '';
    this.successMessage = '';
    this.seriesNotice = '';

    const payload = this.buildPayload();
    const request = this.eventId
      ? this.managementService.updateDraft(this.eventId, payload)
      : this.managementService.createDraft(this.clubId, payload);

    request.subscribe({
      next: (response) => {
        const managedEvent = requireEnvelopeData(response, 'The draft could not be saved.');
        this.applyEvent(managedEvent);
        this.successMessage = this.eventId
          ? 'Draft saved.'
          : 'Draft created. You can keep iterating before publishing.';

        if (!this.eventId) {
          this.eventId = managedEvent.id;
          void this.router.navigate(['/clubs', this.clubId, 'manage', 'events', managedEvent.id], {
            replaceUrl: true,
          });
        }

        this.saving = false;
      },
      error: (error) => {
        this.saving = false;
        this.error =
          error?.error?.message || error?.error?.Message || 'We could not save the draft.';
      },
    });
  }

  publish(): void {
    if (!this.eventId) {
      this.error = 'Save the draft before publishing it.';
      return;
    }

    this.runLifecycleAction(
      () => this.managementService.publishEvent(this.eventId),
      'Event published.',
    );
  }

  cancel(): void {
    if (!this.eventId) {
      return;
    }

    this.runLifecycleAction(
      () => this.managementService.cancelEvent(this.eventId),
      'Event cancelled.',
    );
  }

  archive(): void {
    if (!this.eventId) {
      return;
    }

    this.runLifecycleAction(
      () => this.managementService.archiveEvent(this.eventId),
      'Event archived.',
    );
  }

  lifecycleBadge(lifecycleState: EventLifecycleState): string {
    switch (lifecycleState) {
      case 'Draft':
        return 'bg-amber-500/10 text-amber-700 dark:text-amber-300 border border-amber-500/20';
      case 'Published':
        return 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 border border-emerald-500/20';
      case 'Cancelled':
        return 'bg-rose-500/10 text-rose-700 dark:text-rose-300 border border-rose-500/20';
      case 'Archived':
        return 'bg-slate-500/10 text-slate-700 dark:text-slate-300 border border-slate-500/20';
    }
  }

  private loadExisting(): void {
    this.managementService.getManageableEvent(this.eventId).subscribe({
      next: (response) => {
        const event = requireEnvelopeData(response, 'We could not load this event.');
        this.applyEvent(event);
        this.loading = false;
      },
      error: (error) => {
        this.loading = false;
        this.error =
          error?.error?.message || error?.error?.Message || 'We could not load this event.';
      },
    });
  }

  private applyEvent(event: ManagedEvent): void {
    this.event = event;
    this.clubId = event.clubId;
    this.imageUrls = [...event.imageUrls];
    this.form.patchValue({
      name: event.name ?? '',
      description: event.description ?? '',
      location: event.location ?? '',
      isPrivate: event.isPrivate,
      maxParticipants: event.maxParticipants ?? null,
      registerCost: event.registerCost,
      waitlistEnabled: event.waitlistEnabled ?? false,
      startTime: this.toDateTimeLocal(event.startTime),
      endTime: this.toDateTimeLocal(event.endTime),
      category: event.category,
      venueName: event.venueName ?? '',
      city: event.city ?? '',
      latitude: event.latitude ?? null,
      longitude: event.longitude ?? null,
      tags: event.tags.join(', '),
    });

    this.seedRecurrenceDefaults(event);
  }

  /**
   * Pre-fills the repeat step from the event's own schedule so the organizer does not retype it.
   * Uses the event's stored zone when it has one — an occurrence must keep repeating in the zone
   * its series was anchored to, not in whichever zone the person editing it happens to be in.
   */
  private seedRecurrenceDefaults(event: ManagedEvent): void {
    const scheduled = this.toDateTimeLocal(event.startTime);

    this.recurrenceForm.patchValue(
      {
        startLocalDate: scheduled ? scheduled.slice(0, 10) : '',
        startLocalTime: scheduled ? scheduled.slice(11, 16) : '19:00',
        timeZoneId: event.timeZoneId ?? this.recurrenceForm.controls.timeZoneId.value,
      },
      { emitEvent: false },
    );
  }

  private buildPayload(): EventDraftPayload {
    const raw = this.form.getRawValue();

    return {
      name: this.optionalText(raw.name),
      description: this.optionalText(raw.description),
      location: this.optionalText(raw.location),
      imageUrls: this.imageUrls,
      isPrivate: !!raw.isPrivate,
      maxParticipants: raw.maxParticipants ?? undefined,
      registerCost: raw.registerCost ?? 0,
      waitlistEnabled: !!raw.waitlistEnabled,
      startTime: this.toIso(raw.startTime),
      endTime: raw.endTime ? this.toIso(raw.endTime) : null,
      category: raw.category as EventDraftPayload['category'],
      venueName: this.optionalText(raw.venueName),
      city: this.optionalText(raw.city),
      latitude: raw.latitude,
      longitude: raw.longitude,
      tags: this.parseTags(raw.tags),
    };
  }

  private runLifecycleAction(
    requestFactory: () => ReturnType<EventsManagementService['publishEvent']>,
    successMessage: string,
  ): void {
    this.saving = true;
    this.error = '';
    this.successMessage = '';

    requestFactory().subscribe({
      next: (response) => {
        const event = requireEnvelopeData(response, 'The event lifecycle action failed.');
        this.applyEvent(event);
        this.successMessage = successMessage;
        this.saving = false;
      },
      error: (error) => {
        this.saving = false;
        this.error =
          error?.error?.message ||
          error?.error?.Message ||
          'The lifecycle action could not be completed.';
      },
    });
  }

  /**
   * Pulls `HH:mm` straight out of the `datetime-local` string. Deliberately string slicing
   * rather than `new Date(...)`, which would reinterpret the value in the browser's zone —
   * the backend anchors it to the series' zone instead.
   */
  private toLocalTimeOfDay(value: string | null | undefined): string | undefined {
    const time = value?.slice(11, 16);

    return time && /^\d{2}:\d{2}$/.test(time) ? time : undefined;
  }

  /**
   * Event length in minutes. Both inputs are wall-clock strings in the same zone, so the
   * difference between them is safe to take even though neither is an instant.
   */
  private toDurationMinutes(
    start: string | null | undefined,
    end: string | null | undefined,
  ): number | undefined {
    if (!start || !end) {
      return undefined;
    }

    const startMs = Date.parse(start);
    const endMs = Date.parse(end);

    if (Number.isNaN(startMs) || Number.isNaN(endMs) || endMs <= startMs) {
      return undefined;
    }

    return Math.round((endMs - startMs) / 60000);
  }

  private optionalText(value: string | null | undefined): string | undefined {
    const normalized = value?.trim();
    return normalized ? normalized : undefined;
  }

  private parseTags(value: string | null | undefined): string[] | undefined {
    const tags = (value ?? '')
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean);

    return tags.length > 0 ? tags : undefined;
  }

  private toIso(value: string | null | undefined): string | undefined {
    if (!value) {
      return undefined;
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
  }

  private toDateTimeLocal(value: string | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '';
    }

    const pad = (input: number) => String(input).padStart(2, '0');

    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }
}
