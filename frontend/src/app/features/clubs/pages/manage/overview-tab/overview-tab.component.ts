import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { Club, CLUB_TYPE_STYLES } from '../../../models/club.types';
import { ClubsService } from '../../../services/clubs.service';

@Component({
  selector: 'app-overview-tab',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './overview-tab.component.html',
})
export class OverviewTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  club: Club | null = null;
  loading = true;
  error = '';

  readonly clubTypeStyles = CLUB_TYPE_STYLES;

  constructor(
    private route: ActivatedRoute,
    private clubsService: ClubsService,
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

  memberCapacityPercent(): number {
    if (!this.club || this.club.maxMemberCount <= 0) return 0;
    return Math.min(100, (this.club.memberCount / this.club.maxMemberCount) * 100);
  }

  private load(): void {
    this.loading = true;
    this.error = '';
    this.clubsService
      .getClub(this.clubId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.club = response.data ?? null;
          if (!this.club) this.error = response.message || 'Club not found.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load this club.');
        },
      });
  }
}
