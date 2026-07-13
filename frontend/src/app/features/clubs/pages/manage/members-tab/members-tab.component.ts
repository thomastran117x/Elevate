import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { ClubMember } from '../../../models/club-management.types';
import { Club } from '../../../models/club.types';
import { ClubManagementService } from '../../../services/club-management.service';
import { ClubsService } from '../../../services/clubs.service';

@Component({
  selector: 'app-members-tab',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './members-tab.component.html',
})
export class MembersTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  club: Club | null = null;
  members: ClubMember[] = [];
  loading = true;
  error = '';

  page = 1;
  readonly pageSize = 20;
  totalCount = 0;

  constructor(
    private route: ActivatedRoute,
    private management: ClubManagementService,
    private clubsService: ClubsService,
  ) {}

  ngOnInit(): void {
    this.clubId = Number.parseInt(this.route.parent?.snapshot.paramMap.get('clubId') ?? '', 10) || 0;
    if (!this.clubId) {
      this.loading = false;
      this.error = 'A valid club ID is required.';
      return;
    }
    this.loadClub();
    this.loadMembers();
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  displayName(member: ClubMember): string {
    return member.name || member.username || `User #${member.userId}`;
  }

  initials(member: ClubMember): string {
    const source = member.name || member.username || '';
    return source ? source.slice(0, 2).toUpperCase() : `#${member.userId}`.slice(0, 2);
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages || page === this.page) return;
    this.page = page;
    this.loadMembers();
  }

  private loadClub(): void {
    this.clubsService
      .getClub(this.clubId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.club = response.data ?? null;
        },
        error: () => {
          /* capacity denominator is best-effort */
        },
      });
  }

  private loadMembers(): void {
    this.loading = true;
    this.error = '';
    this.management
      .getMembers(this.clubId, this.page, this.pageSize)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.members = response.data?.items ?? [];
          this.totalCount = response.data?.totalCount ?? 0;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load members.');
        },
      });
  }
}
