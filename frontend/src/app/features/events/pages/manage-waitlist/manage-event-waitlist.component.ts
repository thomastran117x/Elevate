import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { extractEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import { EventWaitlistEntry } from '../../models/event-waitlist.types';
import { EventItem } from '../../models/event.types';
import { EventWaitlistService } from '../../services/event-waitlist.service';
import { EventsService } from '../../services/events.service';

@Component({
  selector: 'app-manage-event-waitlist',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './manage-event-waitlist.component.html',
})
export class ManageEventWaitlistComponent implements OnInit {
  eventId = 0;
  event: EventItem | null = null;
  entries: EventWaitlistEntry[] = [];
  totalCount = 0;
  loading = true;
  actionLoadingId: number | null = null;
  promoteLoading = false;
  error = '';
  notice = '';

  constructor(
    private route: ActivatedRoute,
    private waitlistService: EventWaitlistService,
    private eventsService: EventsService,
  ) {}

  ngOnInit(): void {
    this.eventId = Number(this.route.snapshot.paramMap.get('eventId') ?? 0);
    this.reload();
  }

  get seatsFree(): number {
    if (!this.event || this.event.maxParticipants <= 0) return 0;
    return Math.max(0, this.event.maxParticipants - this.event.registrationCount);
  }

  /** Joins whichever optional attendee details were supplied at join time. */
  entryDetails(entry: EventWaitlistEntry): string {
    return [entry.phoneNumber, entry.dietaryNeeds, entry.notes]
      .filter((value): value is string => !!value && value.trim().length > 0)
      .join(' · ');
  }

  statusClass(entry: EventWaitlistEntry): string {
    switch (entry.status) {
      case 'Promoted':
        return 'text-success';
      case 'Removed':
        return 'text-danger';
      case 'Left':
        return 'text-subtle';
      default:
        return 'text-warning';
    }
  }

  promoteNext(): void {
    if (this.promoteLoading) return;
    this.promoteLoading = true;
    this.error = '';
    this.notice = '';

    this.waitlistService.promoteNext(this.eventId).subscribe({
      next: (result) => {
        this.promoteLoading = false;
        this.notice = `Promoted ${result.promotedCount} person${result.promotedCount === 1 ? '' : 's'} off the waitlist.`;
        this.reload();
      },
      error: (response) => {
        this.promoteLoading = false;
        this.error = getApiClientMessage(response, 'We could not promote from the waitlist.');
      },
    });
  }

  remove(entry: EventWaitlistEntry): void {
    if (this.actionLoadingId !== null) return;
    this.actionLoadingId = entry.id;
    this.error = '';
    this.notice = '';

    this.waitlistService.removeEntry(this.eventId, entry.id).subscribe({
      next: () => {
        this.actionLoadingId = null;
        this.reload();
      },
      error: (response) => {
        this.actionLoadingId = null;
        this.error = getApiClientMessage(response, 'We could not remove this entry.');
      },
    });
  }

  private reload(): void {
    this.loading = true;
    this.error = '';

    forkJoin({
      event: this.eventsService.getEvent(this.eventId),
      waitlist: this.waitlistService.getEventWaitlist(this.eventId),
    }).subscribe({
      next: ({ event, waitlist }) => {
        this.event = extractEnvelopeData(event);
        this.entries = waitlist.entries;
        this.totalCount = waitlist.totalCount;
        this.loading = false;
      },
      error: (response) => {
        this.loading = false;
        this.error = getApiClientMessage(response, 'We could not load the waitlist.');
      },
    });
  }
}
