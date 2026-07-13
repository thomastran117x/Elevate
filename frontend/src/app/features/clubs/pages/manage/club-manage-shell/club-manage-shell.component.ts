import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { Club } from '../../../models/club.types';
import { ClubsService } from '../../../services/clubs.service';

@Component({
  selector: 'app-club-manage-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './club-manage-shell.component.html',
})
export class ClubManageShellComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  club: Club | null = null;
  loading = true;
  error = '';

  readonly tabs = [
    { path: 'overview', label: 'Overview' },
    { path: 'details', label: 'Details' },
    { path: 'staff', label: 'Staff' },
    { path: 'history', label: 'History' },
    { path: 'analytics', label: 'Analytics' },
  ];

  constructor(
    private route: ActivatedRoute,
    private clubsService: ClubsService,
  ) {}

  ngOnInit(): void {
    const clubId = Number.parseInt(this.route.snapshot.paramMap.get('clubId') ?? '', 10);
    if (!Number.isFinite(clubId) || clubId <= 0) {
      this.loading = false;
      this.error = 'A valid club ID is required.';
      return;
    }

    this.clubId = clubId;
    this.loadClub();
  }

  private loadClub(): void {
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
          if (!this.club) {
            this.error = response.message || 'Club not found.';
          }
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load this club.');
        },
      });
  }
}
