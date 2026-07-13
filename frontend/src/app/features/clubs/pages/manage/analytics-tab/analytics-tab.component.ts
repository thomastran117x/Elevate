import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { ClubAnalytics, TrendPoint } from '../../../models/club-management.types';
import { ClubManagementService } from '../../../services/club-management.service';

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

  constructor(
    private route: ActivatedRoute,
    private management: ClubManagementService,
  ) {}

  ngOnInit(): void {
    this.clubId = Number.parseInt(this.route.parent?.snapshot.paramMap.get('clubId') ?? '', 10) || 0;
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
          if (this.analytics) {
            this.registrationSpark = this.buildSparkline(this.analytics.registrationTrend);
            this.revenueSpark = this.buildSparkline(
              this.analytics.revenueTrend.map((p) => ({ date: p.date, value: p.value / 100 })),
            );
          }
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load analytics.');
        },
      });
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
