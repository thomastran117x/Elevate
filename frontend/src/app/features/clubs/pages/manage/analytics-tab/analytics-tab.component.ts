import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { ClubAnalytics, TrendPoint } from '../../../models/club-management.types';
import { ClubManagementService } from '../../../services/club-management.service';

interface TrendWindow {
  label: string;
  days: number; // 0 = all
}

interface Sparkline {
  line: string;
  area: string;
  points: { x: number; y: number; date: string; value: number }[];
  latest: number;
  total: number;
  hasData: boolean;
}

const SPARK_W = 240;
const SPARK_H = 56;
const SPARK_PAD = 4;

@Component({
  selector: 'app-analytics-tab',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './analytics-tab.component.html',
})
export class AnalyticsTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  analytics: ClubAnalytics | null = null;
  loading = true;
  error = '';

  readonly sparkW = SPARK_W;
  readonly sparkH = SPARK_H;

  registrationSpark: Sparkline | null = null;
  revenueSpark: Sparkline | null = null;

  readonly windows: TrendWindow[] = [
    { label: '7d', days: 7 },
    { label: '14d', days: 14 },
    { label: '30d', days: 30 },
    { label: 'All', days: 0 },
  ];
  activeWindow = 30;

  constructor(
    private route: ActivatedRoute,
    private management: ClubManagementService,
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
  }

  /** Revenue values are stored in cents. */
  toDollars(cents: number): number {
    return cents / 100;
  }

  private load(): void {
    this.loading = true;
    this.error = '';
    this.management
      .getAnalytics(this.clubId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.analytics = response.data ?? null;
          this.rebuildSparks();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load analytics.');
        },
      });
  }

  setWindow(days: number): void {
    this.activeWindow = days;
    this.rebuildSparks();
  }

  private windowed(points: TrendPoint[]): TrendPoint[] {
    if (this.activeWindow <= 0 || points.length <= this.activeWindow) return points;
    return points.slice(points.length - this.activeWindow);
  }

  private rebuildSparks(): void {
    if (!this.analytics) {
      this.registrationSpark = null;
      this.revenueSpark = null;
      return;
    }
    this.registrationSpark = this.buildSparkline(this.windowed(this.analytics.registrationTrend));
    this.revenueSpark = this.buildSparkline(
      this.windowed(this.analytics.revenueTrend).map((p) => ({
        date: p.date,
        value: p.value / 100,
      })),
    );
  }

  exportCsv(): void {
    const a = this.analytics;
    if (!a) return;

    const lines: string[] = [];
    const row = (cells: (string | number)[]) =>
      cells.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(',');

    lines.push(row(['Metric', 'Value']));
    lines.push(row(['Total events', a.totalEvents]));
    lines.push(row(['Published events', a.publishedEvents]));
    lines.push(row(['Draft events', a.draftEvents]));
    lines.push(row(['Upcoming events', a.upcomingEvents]));
    lines.push(row(['Total registrations', a.totalRegistrations]));
    lines.push(row(['Unique attendees', a.uniqueAttendees]));
    lines.push(row(['Repeat attendees', a.repeatAttendees]));
    lines.push(row(['Total revenue', this.toDollars(a.totalRevenue).toFixed(2)]));
    lines.push(row(['Pending revenue', this.toDollars(a.pendingRevenue).toFixed(2)]));
    lines.push(row(['Avg fill rate %', a.avgFillRate.toFixed(1)]));

    lines.push('');
    lines.push(row(['Top events by registrations', 'Registrations', 'Fill rate %', 'Revenue']));
    for (const e of a.topEventsByRegistrations) {
      lines.push(
        row([
          e.name,
          e.registrationCount,
          e.fillRate.toFixed(1),
          this.toDollars(e.revenue).toFixed(2),
        ]),
      );
    }

    lines.push('');
    lines.push(row(['Date', 'Registrations', 'Revenue']));
    const revByDate = new Map(a.revenueTrend.map((p) => [p.date, p.value]));
    for (const p of a.registrationTrend) {
      lines.push(row([p.date, p.value, this.toDollars(revByDate.get(p.date) ?? 0).toFixed(2)]));
    }

    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `club-${this.clubId}-analytics.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  private buildSparkline(points: TrendPoint[]): Sparkline {
    const total = points.reduce((sum, p) => sum + p.value, 0);
    const latest = points.length ? points[points.length - 1].value : 0;

    if (points.length < 2) {
      return { line: '', area: '', points: [], latest, total, hasData: false };
    }

    const max = Math.max(...points.map((p) => p.value), 1);
    const min = Math.min(...points.map((p) => p.value), 0);
    const range = max - min || 1;
    const stepX = (SPARK_W - SPARK_PAD * 2) / (points.length - 1);

    const coords = points.map((p, i) => ({
      x: SPARK_PAD + i * stepX,
      y: SPARK_PAD + (SPARK_H - SPARK_PAD * 2) * (1 - (p.value - min) / range),
      date: p.date,
      value: p.value,
    }));

    const line = coords
      .map((c, i) => `${i === 0 ? 'M' : 'L'}${c.x.toFixed(1)} ${c.y.toFixed(1)}`)
      .join(' ');
    const baseline = SPARK_H - SPARK_PAD;
    const area = `${line} L${coords[coords.length - 1].x.toFixed(1)} ${baseline} L${coords[0].x.toFixed(1)} ${baseline} Z`;

    return { line, area, points: coords, latest, total, hasData: true };
  }
}
