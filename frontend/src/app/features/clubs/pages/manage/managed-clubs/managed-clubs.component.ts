import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { Club, CLUB_TYPE_STYLES } from '../../../models/club.types';
import { ClubManagementService } from '../../../services/club-management.service';

@Component({
  selector: 'app-managed-clubs',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './managed-clubs.component.html',
})
export class ManagedClubsComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubs: Club[] = [];
  loading = true;
  error = '';

  readonly clubTypeStyles = CLUB_TYPE_STYLES;

  constructor(private management: ClubManagementService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading = true;
    this.error = '';

    this.management
      .getManagedClubs()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.clubs = response.data ?? [];
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load your clubs.');
        },
      });
  }

  roleLabel(club: Club): string {
    if (club.isOwner) return 'Owner';
    if (club.isManager) return 'Manager';
    if (club.isVolunteer) return 'Volunteer';
    return 'Staff';
  }

  trackByClubId(_index: number, club: Club): number {
    return club.id;
  }
}
