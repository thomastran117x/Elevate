import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { ClubStaff, ClubStaffRole } from '../../../models/club-management.types';
import { ClubManagementService } from '../../../services/club-management.service';

@Component({
  selector: 'app-staff-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './staff-tab.component.html',
})
export class StaffTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  staff: ClubStaff[] = [];
  loading = true;
  error = '';
  success = '';

  // Add staff form
  addUserId: number | null = null;
  addRole: ClubStaffRole = 'Manager';
  adding = false;

  // Remove
  removingUserId: number | null = null;

  // Transfer ownership
  showTransfer = false;
  transferUserId: number | null = null;
  transferConfirm = '';
  transferring = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
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

  get transferConfirmed(): boolean {
    return this.transferConfirm.trim().toUpperCase() === 'TRANSFER';
  }

  private load(): void {
    this.loading = true;
    this.error = '';
    this.management
      .getStaff(this.clubId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.staff = response.data ?? [];
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load staff. Only the owner can manage staff.');
        },
      });
  }

  addStaff(): void {
    const userId = Number(this.addUserId);
    if (!Number.isFinite(userId) || userId <= 0) {
      this.error = 'Enter a valid user ID.';
      return;
    }

    this.adding = true;
    this.error = '';
    this.success = '';

    const request$ =
      this.addRole === 'Volunteer'
        ? this.management.addVolunteer(this.clubId, userId)
        : this.management.addManager(this.clubId, userId);

    request$
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.adding = false)),
      )
      .subscribe({
        next: () => {
          this.success = `User #${userId} added as ${this.addRole.toLowerCase()}.`;
          this.addUserId = null;
          this.load();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to add staff member.');
        },
      });
  }

  removeStaff(member: ClubStaff): void {
    this.removingUserId = member.userId;
    this.error = '';
    this.success = '';

    this.management
      .removeStaff(this.clubId, member.userId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.removingUserId = null)),
      )
      .subscribe({
        next: () => {
          this.success = `User #${member.userId} removed.`;
          this.staff = this.staff.filter((s) => s.userId !== member.userId);
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to remove staff member.');
        },
      });
  }

  transferOwnership(): void {
    const userId = Number(this.transferUserId);
    if (!Number.isFinite(userId) || userId <= 0) {
      this.error = 'Enter a valid user ID to transfer to.';
      return;
    }
    if (!this.transferConfirmed) {
      return;
    }

    this.transferring = true;
    this.error = '';
    this.success = '';

    this.management
      .transferOwnership(this.clubId, userId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.transferring = false)),
      )
      .subscribe({
        next: () => {
          // The current user is no longer the owner — return to the dashboard.
          void this.router.navigate(['/clubs/manage']);
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to transfer ownership.');
        },
      });
  }

  trackByUserId(_index: number, member: ClubStaff): number {
    return member.userId;
  }
}
