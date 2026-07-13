import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import {
  ClubVersionDetail,
  ClubVersionListItem,
} from '../../../models/club-management.types';
import { ClubManagementService } from '../../../services/club-management.service';

@Component({
  selector: 'app-history-tab',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './history-tab.component.html',
})
export class HistoryTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  versions: ClubVersionListItem[] = [];
  loading = true;
  error = '';
  success = '';

  page = 1;
  readonly pageSize = 10;
  totalCount = 0;

  expandedVersion: number | null = null;
  detail: ClubVersionDetail | null = null;
  detailLoading = false;
  rollingBackVersion: number | null = null;

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

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages || page === this.page) return;
    this.page = page;
    this.expandedVersion = null;
    this.detail = null;
    this.load();
  }

  toggleDetail(version: ClubVersionListItem): void {
    if (this.expandedVersion === version.versionNumber) {
      this.expandedVersion = null;
      this.detail = null;
      return;
    }

    this.expandedVersion = version.versionNumber;
    this.detail = null;
    this.detailLoading = true;

    this.management
      .getVersion(this.clubId, version.versionNumber)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.detailLoading = false)),
      )
      .subscribe({
        next: (response) => {
          this.detail = response.data ?? null;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load version detail.');
          this.expandedVersion = null;
        },
      });
  }

  rollback(version: ClubVersionListItem): void {
    this.rollingBackVersion = version.versionNumber;
    this.error = '';
    this.success = '';

    this.management
      .rollback(this.clubId, version.versionNumber)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.rollingBackVersion = null)),
      )
      .subscribe({
        next: (response) => {
          const result = response.data;
          this.success = result
            ? `Rolled back to version ${result.restoredFromVersionNumber} (new version ${result.newVersionNumber}).`
            : 'Rollback complete.';
          this.page = 1;
          this.expandedVersion = null;
          this.detail = null;
          this.load();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to roll back to this version.');
        },
      });
  }

  private load(): void {
    this.loading = true;
    this.error = '';
    this.management
      .getVersions(this.clubId, this.page, this.pageSize)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.versions = response.data?.items ?? [];
          this.totalCount = response.data?.totalCount ?? 0;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load version history.');
        },
      });
  }

  trackByVersion(_index: number, version: ClubVersionListItem): number {
    return version.versionNumber;
  }
}
