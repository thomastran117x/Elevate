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
import { EventSeriesService } from '../../services/event-series.service';

/**
 * Series overview: the repeat rule, every occurrence, and the actions that apply to the group.
 * Per-occurrence editing deliberately links out to the ordinary event editor.
 */
@Component({
  selector: 'app-manage-event-series',
  standalone: true,
  imports: [CommonModule, RouterLink],
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

  publishAll(): void {
    this.run(
      () => this.seriesService.publishSeries(this.seriesId),
      (result) =>
        `${result.affectedCount} ${result.affectedCount === 1 ? 'occurrence' : 'occurrences'} published.`,
    );
  }

  cancelSeries(): void {
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

  deleteSeries(scope: EventSeriesDeleteScope): void {
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
