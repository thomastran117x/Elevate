import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { finalize } from 'rxjs/operators';

import { extractEnvelopeData } from '../../../../../core/api/models/api-envelope.model';
import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import {
  ALL_LIFECYCLE_STATES,
  EventLifecycleState,
  ManagedEvent,
} from '../../../../events/models/event.types';
import { EventsManagementService } from '../../../../events/services/events-management.service';

@Component({
  selector: 'app-events-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './events-tab.component.html',
})
export class EventsTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  events: ManagedEvent[] = [];
  loading = true;
  error = '';

  selectedLifecycle: EventLifecycleState | null = null;
  page = 1;
  readonly pageSize = 9;
  totalCount = 0;

  eventSearch = '';
  private readonly searchInput$ = new Subject<string>();

  readonly lifecycles = ALL_LIFECYCLE_STATES;
  readonly skeletons = Array.from({ length: 6 });

  constructor(
    private route: ActivatedRoute,
    private management: EventsManagementService,
  ) {}

  ngOnInit(): void {
    this.clubId =
      Number.parseInt(this.route.parent?.snapshot.paramMap.get('clubId') ?? '', 10) || 0;
    if (!this.clubId) {
      this.loading = false;
      this.error = 'A valid club ID is required.';
      return;
    }
    this.load();

    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page = 1;
        this.load();
      });
  }

  onEventSearch(value: string): void {
    this.eventSearch = value;
    this.searchInput$.next(value);
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  setLifecycle(lifecycle: EventLifecycleState | null): void {
    this.selectedLifecycle = lifecycle;
    this.page = 1;
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages || page === this.page) return;
    this.page = page;
    this.load();
  }

  lifecycleBadge(state: EventLifecycleState): string {
    switch (state) {
      case 'Draft':
        return 'bg-amber-500/10 text-amber-700 dark:text-amber-300 border-amber-500/20';
      case 'Published':
        return 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 border-emerald-500/20';
      case 'Cancelled':
        return 'bg-rose-500/10 text-rose-700 dark:text-rose-300 border-rose-500/20';
      case 'Archived':
        return 'bg-slate-500/10 text-slate-700 dark:text-slate-300 border-slate-500/20';
    }
  }

  private load(): void {
    this.loading = true;
    this.error = '';
    this.management
      .getManageableEvents(this.clubId, {
        lifecycleState: this.selectedLifecycle,
        page: this.page,
        pageSize: this.pageSize,
        search: this.eventSearch || undefined,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          const data = extractEnvelopeData(response);
          this.events = data?.items ?? [];
          this.totalCount = data?.totalCount ?? 0;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load events.');
        },
      });
  }
}
