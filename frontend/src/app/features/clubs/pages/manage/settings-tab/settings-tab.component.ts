import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { Club } from '../../../models/club.types';
import { ClubManagementService } from '../../../services/club-management.service';
import { ClubsService } from '../../../services/clubs.service';

@Component({
  selector: 'app-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './settings-tab.component.html',
})
export class SettingsTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  club: Club | null = null;
  loading = true;
  error = '';

  // Transfer ownership
  showTransfer = false;
  transferIdentifier = '';
  transferConfirm = '';
  transferring = false;

  // Delete
  showDelete = false;
  deleteConfirm = '';
  deleting = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private management: ClubManagementService,
    private clubsService: ClubsService,
  ) {}

  ngOnInit(): void {
    this.clubId =
      Number.parseInt(this.route.parent?.snapshot.paramMap.get('clubId') ?? '', 10) || 0;
    if (!this.clubId) {
      this.loading = false;
      this.error = 'A valid club ID is required.';
      return;
    }
    this.loadClub();
  }

  get transferConfirmed(): boolean {
    return this.transferConfirm.trim().toUpperCase() === 'TRANSFER';
  }

  get deleteConfirmed(): boolean {
    return !!this.club && this.deleteConfirm.trim() === this.club.name;
  }

  private loadClub(): void {
    this.loading = true;
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

  transferOwnership(): void {
    const identifier = this.transferIdentifier.trim();
    if (!identifier) {
      this.error = 'Enter the new owner’s username or email.';
      return;
    }
    if (!this.transferConfirmed) return;

    this.transferring = true;
    this.error = '';

    this.management
      .transferOwnership(this.clubId, identifier)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.transferring = false)),
      )
      .subscribe({
        // The current user is no longer the owner — return to the dashboard.
        next: () => void this.router.navigate(['/clubs/manage']),
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to transfer ownership.');
        },
      });
  }

  deleteClub(): void {
    if (!this.deleteConfirmed) return;

    this.deleting = true;
    this.error = '';

    this.management
      .deleteClub(this.clubId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.deleting = false)),
      )
      .subscribe({
        next: () => void this.router.navigate(['/clubs/manage']),
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to delete the club.');
        },
      });
  }
}
