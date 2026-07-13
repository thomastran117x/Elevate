import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { EmptyStateComponent } from '@common/empty-state/empty-state.component';
import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { Club, CLUB_TYPE_STYLES } from '../../../models/club.types';
import { ClubManagementService } from '../../../services/club-management.service';

type RoleFilter = 'all' | 'owned' | 'staffed';
type SortKey = 'name' | 'members' | 'events';

@Component({
  selector: 'app-managed-clubs',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, EmptyStateComponent],
  templateUrl: './managed-clubs.component.html',
})
export class ManagedClubsComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubs: Club[] = [];
  loading = true;
  error = '';

  search = '';
  roleFilter: RoleFilter = 'all';
  sortBy: SortKey = 'name';

  readonly clubTypeStyles = CLUB_TYPE_STYLES;
  readonly skeletons = Array.from({ length: 6 });

  constructor(private management: ClubManagementService) {}

  ngOnInit(): void {
    this.load();
  }

  get ownedCount(): number {
    return this.clubs.filter((c) => c.isOwner).length;
  }

  get staffedCount(): number {
    return this.clubs.filter((c) => !c.isOwner).length;
  }

  get filteredClubs(): Club[] {
    const term = this.search.trim().toLowerCase();
    return this.clubs
      .filter((club) => {
        if (this.roleFilter === 'owned' && !club.isOwner) return false;
        if (this.roleFilter === 'staffed' && club.isOwner) return false;
        if (term && !`${club.name} ${club.description}`.toLowerCase().includes(term)) return false;
        return true;
      })
      .sort((a, b) => {
        switch (this.sortBy) {
          case 'members':
            return b.memberCount - a.memberCount;
          case 'events':
            return b.eventCount - a.eventCount;
          default:
            return a.name.localeCompare(b.name);
        }
      });
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
