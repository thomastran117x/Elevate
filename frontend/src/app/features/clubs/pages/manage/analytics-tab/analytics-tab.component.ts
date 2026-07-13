import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { ClubAnalytics } from '../../../models/club-management.types';
import { ClubManagementService } from '../../../services/club-management.service';

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
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load analytics.');
        },
      });
  }
}
