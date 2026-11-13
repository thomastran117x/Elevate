import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { User } from '../../../../../core/stores/user.model';
import { selectUser } from '../../../../../core/stores/user.selectors';
import { ConfirmDialogComponent } from '../../../../../shared/components/confirm-dialog/confirm-dialog.component';
import { ClubReview } from '../../../models/club-review.types';
import { Club, isClubStaff } from '../../../models/club.types';
import { ClubReviewsService } from '../../../services/club-reviews.service';
import { ClubsService } from '../../../services/clubs.service';

@Component({
  selector: 'app-reviews-tab',
  standalone: true,
  imports: [CommonModule, RouterLink, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './reviews-tab.component.html',
})
export class ReviewsTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  clubId = 0;
  club: Club | null = null;
  reviews: ClubReview[] = [];
  loading = true;
  error = '';
  success = '';
  currentUser: User | null = null;

  /** The review a moderator is removing, while the reason dialog is open. */
  removingReview: ClubReview | null = null;
  reviewRemovalReason = '';
  reviewRemovalBusy = false;

  page = 1;
  readonly pageSize = 20;
  totalCount = 0;
  readonly skeletons = Array.from({ length: 5 });

  constructor(
    private route: ActivatedRoute,
    private reviewsService: ClubReviewsService,
    private clubsService: ClubsService,
    private store: Store,
  ) {}

  ngOnInit(): void {
    this.clubId =
      Number.parseInt(this.route.parent?.snapshot.paramMap.get('clubId') ?? '', 10) || 0;
    if (!this.clubId) {
      this.loading = false;
      this.error = 'A valid club ID is required.';
      return;
    }
    this.store
      .select(selectUser)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((user) => (this.currentUser = user));
    this.loadClub();
    this.loadReviews();
  }

  /** Staff and admins may remove someone else's review; the server checks their rank. */
  canRemoveReview(review: ClubReview): boolean {
    return (
      this.currentUser != null && review.userId !== this.currentUser.Id && isClubStaff(this.club)
    );
  }

  openRemoveReview(review: ClubReview): void {
    this.error = '';
    this.success = '';
    this.reviewRemovalReason = '';
    this.removingReview = review;
  }

  resolveRemoveReview(confirmed: boolean): void {
    const review = this.removingReview;
    if (!confirmed || !review) {
      this.removingReview = null;
      return;
    }

    this.reviewRemovalBusy = true;
    this.reviewsService
      .deleteReview(this.clubId, review.id, this.reviewRemovalReason.trim())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.reviewRemovalBusy = false;
          this.removingReview = null;
        }),
      )
      .subscribe({
        next: () => {
          this.success = 'Review removed.';
          this.loadClub();
          this.loadReviews();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to remove this review.');
        },
      });
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  reviewerName(review: ClubReview): string {
    return review.name || review.usernameDisplay || review.username || `User #${review.userId}`;
  }

  reviewerInitials(review: ClubReview): string {
    const source = review.name || review.username || '';
    return source ? source.slice(0, 2).toUpperCase() : `#${review.userId}`.slice(0, 2);
  }

  starsFor(rating: number): number[] {
    return [1, 2, 3, 4, 5].map((n) => (n <= Math.round(rating) ? 1 : 0));
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages || page === this.page) return;
    this.page = page;
    this.loadReviews();
  }

  private loadClub(): void {
    this.clubsService
      .getClub(this.clubId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.club = response.data ?? null;
        },
        // The average-rating summary is best-effort.
        error: () => {
          /* no-op */
        },
      });
  }

  private loadReviews(): void {
    this.loading = true;
    this.error = '';
    this.reviewsService
      .getReviews(this.clubId, this.page, this.pageSize)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          this.reviews = response.data?.items ?? [];
          this.totalCount = response.data?.totalCount ?? 0;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load reviews.');
        },
      });
  }
}
