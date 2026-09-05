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
import { lifecycleBadgeClass } from '../../../../events/models/event-lifecycle';
import { EventLifecycleActionsComponent } from '../../../../events/components/lifecycle-actions/lifecycle-actions.component';
import { EventsManagementService } from '../../../../events/services/events-management.service';

@Component({
  selector: 'app-events-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, EventLifecycleActionsComponent],
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
    return lifecycleBadgeClass(state);
  }

  /** Id of the event whose lifecycle request is in flight, so only its card greys out. */
  transitioningEventId: number | null = null;

  /**
   * Runs a lifecycle move from the card grid, then swaps the returned event into the list in
   * place so the card's buttons and badge update without a full reload.
   */
  runLifecycleTransition(event: ManagedEvent, key: string): void {
    this.transitioningEventId = event.id;
    this.error = '';

    this.management
      .runTransition(event.id, key)
      .pipe(
        finalize(() => (this.transitioningEventId = null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (response) => {
          const updated = extractEnvelopeData(response);
          if (!updated) {
            return;
          }

          // A transition can move an event out of the filter that is showing it. Swapping the
          // card in place would leave a Paused card sitting in the Published results with a
          // stale total, so reload instead and let the server decide what belongs on the page.
          if (this.selectedLifecycle && updated.lifecycleState !== this.selectedLifecycle) {
            this.load();
            return;
          }

          this.events = this.events.map((item) => (item.id === updated.id ? updated : item));
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'That change could not be applied.');
        },
      });
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
