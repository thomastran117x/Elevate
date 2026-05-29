import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { Subject, take, takeUntil } from 'rxjs';

import { extractEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import { UserState } from '../../../../core/stores/user.reducer';
import { selectUser } from '../../../../core/stores/user.selectors';
import { EventItem, CATEGORY_STYLES } from '../../models/event.types';
import {
  EventRegistrationService,
  RegistrationDetails,
} from '../../services/event-registration.service';
import { EventsService } from '../../services/events.service';

@Component({
  selector: 'app-event-detail',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './event-detail.component.html',
})
export class EventDetailComponent implements OnInit, OnDestroy {
  event: EventItem | null = null;
  loading = true;
  error = '';
  selectedImageIndex = 0;
  returnQueryParams: Record<string, string> = {};

  isRegistered = false;
  registrationLoading = false;
  registrationError = '';
  currentUserId: number | null = null;
  showRegistrationForm = false;
  isEditing = false;
  registrationDetails: RegistrationDetails | null = null;

  registrationForm: FormGroup;

  readonly categoryStyles = CATEGORY_STYLES;

  private readonly destroy$ = new Subject<void>();
  private requestVersion = 0;

  constructor(
    private eventsService: EventsService,
    private registrationService: EventRegistrationService,
    private store: Store<{ user: UserState }>,
    private route: ActivatedRoute,
    private router: Router,
    private fb: FormBuilder,
  ) {
    this.registrationForm = this.fb.group({
      notes: [''],
      phoneNumber: [''],
      dietaryNeeds: [''],
    });
  }

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      this.returnQueryParams = params.keys.reduce<Record<string, string>>((accumulator, key) => {
        const value = params.get(key);
        if (value !== null) {
          accumulator[key] = value;
        }

        return accumulator;
      }, {});
    });

    this.route.paramMap
      .pipe(takeUntil(this.destroy$))
      .subscribe((params) => this.loadEventFromParams(params));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get heroImage(): string | null {
    if (!this.event?.imageUrls?.length) {
      return null;
    }

    return this.event.imageUrls[this.selectedImageIndex] ?? this.event.imageUrls[0] ?? null;
  }

  get canRegister(): boolean {
    if (!this.event) return false;
    if (this.event.lifecycleState !== 'Published') return false;
    if (this.isEventStarted(this.event)) return false;
    if (this.event.registerCost > 0) return false;
    if (this.event.maxParticipants > 0 && this.event.registrationCount >= this.event.maxParticipants)
      return false;
    return true;
  }

  isEventStarted(event: EventItem): boolean {
    return new Date(event.startTime) <= new Date();
  }

  goBack(): void {
    this.router.navigate(['/events'], {
      queryParams: this.returnQueryParams,
    });
  }

  selectImage(index: number): void {
    this.selectedImageIndex = index;
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString('en-CA', {
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  formatSchedule(event: EventItem): string {
    const start = `${this.formatDate(event.startTime)} at ${this.formatTime(event.startTime)}`;
    if (!event.endTime) {
      return start;
    }

    return `${start} - ${this.formatTime(event.endTime)}`;
  }

  formatCost(cost: number): string {
    return cost === 0 ? 'Free' : `$${cost}`;
  }

  registrationPercent(event: EventItem): number {
    if (event.maxParticipants <= 0) {
      return 0;
    }

    return Math.min(100, (event.registrationCount / event.maxParticipants) * 100);
  }

  openRegistrationForm(): void {
    this.isEditing = false;
    this.registrationForm.reset();
    this.showRegistrationForm = true;
    this.registrationError = '';
  }

  openEditForm(): void {
    this.isEditing = true;
    this.registrationForm.patchValue({
      notes: this.registrationDetails?.notes ?? '',
      phoneNumber: this.registrationDetails?.phoneNumber ?? '',
      dietaryNeeds: this.registrationDetails?.dietaryNeeds ?? '',
    });
    this.showRegistrationForm = true;
    this.registrationError = '';
  }

  closeRegistrationForm(): void {
    this.showRegistrationForm = false;
  }

  submitRegistration(): void {
    if (!this.event || this.registrationLoading) return;
    this.registrationLoading = true;
    this.registrationError = '';

    const details: RegistrationDetails = {
      notes: this.registrationForm.value.notes || undefined,
      phoneNumber: this.registrationForm.value.phoneNumber || undefined,
      dietaryNeeds: this.registrationForm.value.dietaryNeeds || undefined,
    };

    this.registrationService
      .register(this.event.id, details)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isRegistered = true;
          this.registrationLoading = false;
          this.showRegistrationForm = false;
          this.registrationDetails = details;
          if (this.event) {
            this.event = { ...this.event, registrationCount: this.event.registrationCount + 1 };
          }
        },
        error: (response) => {
          this.registrationLoading = false;
          this.registrationError =
            response?.error?.message || response?.error?.Message || 'Registration failed.';
        },
      });
  }

  submitUpdate(): void {
    if (!this.event || this.registrationLoading) return;
    this.registrationLoading = true;
    this.registrationError = '';

    const details: RegistrationDetails = {
      notes: this.registrationForm.value.notes || undefined,
      phoneNumber: this.registrationForm.value.phoneNumber || undefined,
      dietaryNeeds: this.registrationForm.value.dietaryNeeds || undefined,
    };

    this.registrationService
      .updateRegistration(this.event.id, details)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.registrationLoading = false;
          this.showRegistrationForm = false;
          this.registrationDetails = details;
        },
        error: (response) => {
          this.registrationLoading = false;
          this.registrationError =
            response?.error?.message ||
            response?.error?.Message ||
            'Failed to update registration details.';
        },
      });
  }

  unregister(): void {
    if (!this.event || this.registrationLoading) return;
    this.registrationLoading = true;
    this.registrationError = '';

    this.registrationService
      .unregister(this.event.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isRegistered = false;
          this.registrationLoading = false;
          this.registrationDetails = null;
          if (this.event) {
            this.event = {
              ...this.event,
              registrationCount: Math.max(0, this.event.registrationCount - 1),
            };
          }
        },
        error: (response) => {
          this.registrationLoading = false;
          this.registrationError =
            response?.error?.message || response?.error?.Message || 'Unregistration failed.';
        },
      });
  }

  private loadRegistrationStatus(eventId: number): void {
    this.store
      .select(selectUser)
      .pipe(take(1))
      .subscribe((user) => {
        this.currentUserId = user?.Id ?? null;
        if (!user) {
          this.isRegistered = false;
          return;
        }

        this.registrationService
          .checkRegistration(eventId)
          .pipe(takeUntil(this.destroy$))
          .subscribe({
            next: (registered) => {
              this.isRegistered = registered;
            },
            error: () => {
              this.isRegistered = false;
            },
          });
      });
  }

  private loadEventFromParams(params: ParamMap): void {
    const eventId = this.parseEventId(params.get('eventId'));
    if (eventId === null) {
      this.event = null;
      this.loading = false;
      this.error = 'Invalid event ID.';
      return;
    }

    this.fetch(eventId);
  }

  private fetch(eventId: number): void {
    const requestVersion = ++this.requestVersion;
    this.loading = true;
    this.error = '';

    this.eventsService
      .getEvent(eventId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (requestVersion !== this.requestVersion) {
            return;
          }

          const event = extractEnvelopeData(response);
          if (!event) {
            this.event = null;
            this.loading = false;
            this.error = response.message || response.Message || 'Failed to load the event.';
            return;
          }

          this.event = event;
          this.selectedImageIndex = 0;
          this.loading = false;
          this.loadRegistrationStatus(event.id);
        },
        error: (response) => {
          if (requestVersion !== this.requestVersion) {
            return;
          }

          this.event = null;
          this.loading = false;
          this.error =
            response?.error?.message || response?.error?.Message || 'Failed to load the event.';
        },
      });
  }

  private parseEventId(value: string | null): number | null {
    if (!value) {
      return null;
    }

    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
  }
}
