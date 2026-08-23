import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { requireEnvelopeData } from '../../../../core/api/models/api-envelope.model';
import {
  EventLifecycleState,
  EventSeries,
  EventSeriesDeleteScope,
  ManagedEvent,
} from '../../models/event.types';
import { lifecycleBadgeClass } from '../../models/event-lifecycle';
import { ConfirmDialogComponent } from '../../../../shared/components/confirm-dialog/confirm-dialog.component';
import { EventSeriesService } from '../../services/event-series.service';

/** A confirmed-before-it-runs action that applies to every occurrence at once. */
interface SeriesBulkAction {
  key: 'publishAll' | 'cancelSeries' | 'deleteSeries';
  title: string;
  confirmLabel: string;
  tone: 'danger' | 'primary';
  impacts: string[];
  reversibleNote: string | null;
  requireTypedConfirmation?: string;
}

/**
 * Series overview: the repeat rule, every occurrence, and the actions that apply to the group.
 * Per-occurrence editing deliberately links out to the ordinary event editor.
 */
@Component({
  selector: 'app-manage-event-series',
  standalone: true,
  imports: [CommonModule, RouterLink, ConfirmDialogComponent],
  templateUrl: './manage-event-series.component.html',
})
export class ManageEventSeriesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly seriesService = inject(EventSeriesService);

  clubId = 0;
  seriesId = 0;
  series: EventSeries | null = null;
  loading = true;
  working = false;
  error = '';
  successMessage = '';
  notice = '';

  ngOnInit(): void {
    this.clubId = Number.parseInt(
      this.route.snapshot.paramMap.get('clubId') ??
        this.route.parent?.snapshot.paramMap.get('clubId') ??
        '',
      10,
    );
    this.seriesId = Number.parseInt(this.route.snapshot.paramMap.get('seriesId') ?? '', 10);

    if (!Number.isFinite(this.seriesId) || this.seriesId <= 0) {
      this.loading = false;
      this.error = 'A valid series ID is required.';
      return;
    }

    this.load();
  }

  get occurrences(): ManagedEvent[] {
    return this.series?.occurrences ?? [];
  }

  get draftCount(): number {
    return this.occurrences.filter((o) => o.lifecycleState === 'Draft').length;
  }

  get ruleSummary(): string {
    const rule = this.series?.rule;

    if (!rule) {
      return '';
    }

    const cadence =
      rule.interval === 1
        ? rule.frequency.toLowerCase()
        : `every ${rule.interval} ${rule.frequency === 'Daily' ? 'days' : rule.frequency === 'Weekly' ? 'weeks' : 'months'}`;

    const ending =
      rule.endMode === 'Count'
        ? `${rule.occurrenceCount} occurrences`
        : `until ${rule.endLocalDate}`;

    return `Repeats ${cadence}, ${ending}, in ${rule.timeZoneId}`;
  }

  lifecycleBadge(lifecycleState: EventLifecycleState): string {
    return lifecycleBadgeClass(lifecycleState);
  }

  private publishAll(): void {
    this.run(
      () => this.seriesService.publishSeries(this.seriesId),
      (result) =>
        `${result.affectedCount} ${result.affectedCount === 1 ? 'occurrence' : 'occurrences'} published.`,
    );
  }

  /**
   * The bulk action awaiting confirmation. Series actions hit every occurrence at once, so the
   * prompt has to say how many and what survives — unlike single events, the server has no
   * per-transition descriptor to lean on here.
   */
  pendingBulkAction: SeriesBulkAction | null = null;

  /**
   * The occurrences a cancel request will actually change.
   *
   * Mirrors the backend's futureOnly skip: counting occurrences that have already started, or
   * that are not in a cancellable state, would promise more than the request delivers.
   */
  private get cancellableOccurrences(): ManagedEvent[] {
    const now = Date.now();

    return (this.series?.occurrences ?? []).filter(
      (occurrence) =>
        (occurrence.lifecycleState === 'Published' || occurrence.lifecycleState === 'Paused') &&
        !!occurrence.startTime &&
        new Date(occurrence.startTime).getTime() > now,
    );
  }

  private get cancellableCount(): number {
    return this.cancellableOccurrences.length;
  }

  private get registeredOccurrenceCount(): number {
    return this.cancellableOccurrences.filter((occurrence) => occurrence.registrationCount > 0)
      .length;
  }

  askPublishAll(): void {
    this.pendingBulkAction = {
      key: 'publishAll',
      title: `Publish ${this.draftCount} ${this.draftCount === 1 ? 'occurrence' : 'occurrences'}?`,
      confirmLabel: 'Publish all',
      tone: 'primary',
      impacts: [
        'Every draft occurrence in this series becomes visible publicly and opens for registration.',
        'Occurrences that are not ready to publish are skipped and reported back to you.',
      ],
      reversibleNote: 'Reversible — you can pause or cancel occurrences individually afterwards.',
    };
  }

  askCancelSeries(): void {
    const impacts = [
      `${this.cancellableCount} ${this.cancellableCount === 1 ? 'occurrence' : 'occurrences'} will be cancelled.`,
      'Each is removed from public listings and closed to new registrations.',
      'Occurrences that have already started are left alone.',
    ];

    const registered = this.registeredOccurrenceCount;
    if (registered > 0) {
      impacts.unshift(
        `${registered} ${registered === 1 ? 'occurrence has' : 'occurrences have'} people registered. Their registrations stay on record.`,
      );
    }

    this.pendingBulkAction = {
      key: 'cancelSeries',
      title: 'Cancel this whole series?',
      confirmLabel: 'Cancel series',
      tone: 'danger',
      impacts,
      reversibleNote: 'Reversible — occurrences can be reinstated one at a time afterwards.',
    };
  }

  askDeleteSeries(): void {
    this.pendingBulkAction = {
      key: 'deleteSeries',
      title: 'Delete this series?',
      confirmLabel: 'Delete series',
      tone: 'danger',
      impacts: [
        'Unbooked future draft occurrences are permanently deleted, along with their images.',
        'Occurrences anyone has registered for are kept and become standalone events.',
        'Occurrences that have already happened are not touched.',
      ],
      // The one genuinely unrecoverable action on this screen.
      reversibleNote: null,
      requireTypedConfirmation: 'DELETE',
    };
  }

  onBulkActionResolved(confirmed: boolean): void {
    const action = this.pendingBulkAction;
    this.pendingBulkAction = null;

    if (!confirmed || !action) {
      return;
    }

    switch (action.key) {
      case 'publishAll':
        this.publishAll();
        return;
      case 'cancelSeries':
        this.cancelSeries();
        return;
      case 'deleteSeries':
        this.deleteSeries('FutureDrafts');
        return;
    }
  }

  private cancelSeries(): void {
    this.run(
      () => this.seriesService.cancelSeries(this.seriesId, true),
      (result) =>
        `${result.affectedCount} ${result.affectedCount === 1 ? 'occurrence' : 'occurrences'} cancelled.`,
    );
  }

  extend(count: number): void {
    this.working = true;
    this.resetMessages();

    this.seriesService.extendSeries(this.seriesId, { occurrenceCount: count }).subscribe({
      next: (response) => {
        this.series = requireEnvelopeData(response, 'The series could not be extended.');
        this.successMessage = `The series now has ${this.series.occurrences.length} occurrences.`;
        this.working = false;
      },
      error: (error) => this.fail(error, 'We could not extend the series.'),
    });
  }

  detach(occurrence: ManagedEvent): void {
    this.working = true;
    this.resetMessages();

    this.seriesService.detachOccurrence(occurrence.id).subscribe({
      next: () => {
        this.successMessage = 'That occurrence is now a standalone event.';
        this.working = false;
        this.load();
      },
      error: (error) => this.fail(error, 'We could not detach that occurrence.'),
    });
  }

  private deleteSeries(scope: EventSeriesDeleteScope): void {
    this.working = true;
    this.resetMessages();

    this.seriesService.deleteSeries(this.seriesId, scope).subscribe({
      next: (response) => {
        const result = requireEnvelopeData(response, 'The series could not be deleted.');
        this.working = false;

        void this.router.navigate(['/clubs', this.clubId, 'manage', 'events'], {
          state: { seriesDeleted: result.seriesId },
        });
      },
      error: (error) => this.fail(error, 'We could not delete the series.'),
    });
  }

  private load(): void {
    this.seriesService.getSeries(this.seriesId).subscribe({
      next: (response) => {
        this.series = requireEnvelopeData(response, 'We could not load this series.');
        this.clubId = this.series.clubId;
        this.loading = false;
      },
      error: (error) => {
        this.loading = false;
        this.error =
          error?.error?.message || error?.error?.Message || 'We could not load this series.';
      },
    });
  }

  private run(
    request: () => ReturnType<EventSeriesService['publishSeries']>,
    describe: (result: { affectedCount: number }) => string,
  ): void {
    this.working = true;
    this.resetMessages();

    request().subscribe({
      next: (response) => {
        const result = requireEnvelopeData(response, 'The action could not be completed.');
        this.successMessage = describe(result);
        this.notice = this.seriesService.describeSkipped(result) ?? '';
        this.working = false;
        this.load();
      },
      error: (error) => this.fail(error, 'The action could not be completed.'),
    });
  }

  private resetMessages(): void {
    this.error = '';
    this.successMessage = '';
    this.notice = '';
  }

  private fail(error: unknown, fallback: string): void {
    this.working = false;

    const payload = error as { error?: { message?: string; Message?: string } };
    this.error = payload?.error?.message || payload?.error?.Message || fallback;
  }
}
