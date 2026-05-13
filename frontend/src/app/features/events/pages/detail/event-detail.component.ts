import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { EventItem, CATEGORY_STYLES } from '../../models/event.types';
import { EventsService } from '../../services/events.service';
import { extractEnvelopeData } from '../../../../core/api/models/api-envelope.model';

@Component({
  selector: 'app-event-detail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './event-detail.component.html',
})
export class EventDetailComponent implements OnInit, OnDestroy {
  event: EventItem | null = null;
  loading = true;
  error = '';
  selectedImageIndex = 0;
  returnQueryParams: Record<string, string> = {};

  readonly categoryStyles = CATEGORY_STYLES;

  private readonly destroy$ = new Subject<void>();
  private requestVersion = 0;

  constructor(
    private eventsService: EventsService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.route.queryParamMap
      .pipe(takeUntil(this.destroy$))
      .subscribe((params) => {
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
        },
        error: (response) => {
          if (requestVersion !== this.requestVersion) {
            return;
          }

          this.event = null;
          this.loading = false;
          this.error =
            response?.error?.message ||
            response?.error?.Message ||
            'Failed to load the event.';
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
