import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import { ALL_LIFECYCLE_STATES, EventLifecycleState, ManagedEvent } from '../../models/event.types';
import { EventsManagementService } from '../../services/events-management.service';

@Component({
  selector: 'app-manage-club-events',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './manage-club-events.component.html',
})
export class ManageClubEventsComponent {
  clubId = 0;
  loading = true;
  error = '';
  selectedLifecycle: EventLifecycleState | null = null;
  events: ManagedEvent[] = [];
  totalCount = 0;
  currentPage = 1;
  readonly pageSize = 12;
  readonly lifecycles = ALL_LIFECYCLE_STATES;

  constructor(
    private route: ActivatedRoute,
    private managementService: EventsManagementService,
  ) {}

  ngOnInit(): void {
    const clubId = Number.parseInt(this.route.snapshot.paramMap.get('clubId') ?? '', 10);
    if (!Number.isFinite(clubId) || clubId <= 0) {
      this.loading = false;
      this.error = 'A valid club ID is required.';
      return;
    }

    this.clubId = clubId;
    this.load();
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  setLifecycle(lifecycleState: EventLifecycleState | null): void {
    this.selectedLifecycle = lifecycleState;
    this.currentPage = 1;
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages || page === this.currentPage) {
      return;
    }

    this.currentPage = page;
    this.load();
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

  trackByEventId(_index: number, event: ManagedEvent): number {
    return event.id;
  }

  private load(): void {
    this.loading = true;
    this.error = '';

    this.managementService
      .getManageableEvents(this.clubId, {
        lifecycleState: this.selectedLifecycle,
        page: this.currentPage,
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (response) => {
          const data = extractEnvelopeData(response);
          this.events = data?.items ?? [];
          this.totalCount = data?.totalCount ?? 0;
          this.loading = false;
        },
        error: (error) => {
          this.loading = false;
          this.error =
            error?.error?.message || error?.error?.Message || 'We could not load the club events.';
        },
      });
  }
}
