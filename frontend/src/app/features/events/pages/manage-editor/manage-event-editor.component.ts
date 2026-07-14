import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { requireEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import {
  ALL_CATEGORIES,
  EventDraftPayload,
  EventLifecycleState,
  ManagedEvent,
} from '../../models/event.types';
import { EventsManagementService } from '../../services/events-management.service';

@Component({
  selector: 'app-manage-event-editor',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './manage-event-editor.component.html',
})
export class ManageEventEditorComponent {
  private readonly fb = new FormBuilder();

  readonly categories = ALL_CATEGORIES;
  readonly inputClass =
    'w-full rounded-2xl border border-line bg-glass-strong px-4 py-3 text-base text-content placeholder:text-faint outline-none transition focus:border-accent focus:ring-4 focus:ring-accent/15';
  readonly form = this.fb.group({
    name: ['', [Validators.minLength(3), Validators.maxLength(30)]],
    description: ['', [Validators.maxLength(200)]],
    location: ['', [Validators.maxLength(50)]],
    isPrivate: [false],
    maxParticipants: [null as number | null],
    registerCost: [0],
    startTime: [''],
    endTime: [''],
    category: ['Other'],
    venueName: [''],
    city: [''],
    latitude: [null as number | null],
    longitude: [null as number | null],
    tags: [''],
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

  // Wizard state
  readonly steps = [
    { label: 'Basics', hint: 'Name, category & description' },
    { label: 'Schedule', hint: 'When it happens' },
    { label: 'Location', hint: 'Where it happens' },
    { label: 'Details', hint: 'Capacity, pricing & tags' },
    { label: 'Images', hint: 'Optional gallery' },
    { label: 'Review', hint: 'Confirm & submit' },
  ];
  currentStep = 0;

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
  ) {}

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
    this.saving = true;
    this.error = '';
    this.successMessage = '';

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
      startTime: this.toDateTimeLocal(event.startTime),
      endTime: this.toDateTimeLocal(event.endTime),
      category: event.category,
      venueName: event.venueName ?? '',
      city: event.city ?? '',
      latitude: event.latitude ?? null,
      longitude: event.longitude ?? null,
      tags: event.tags.join(', '),
    });
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
