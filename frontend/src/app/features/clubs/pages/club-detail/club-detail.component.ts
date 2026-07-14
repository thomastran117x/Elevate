import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { Subject, takeUntil } from 'rxjs';

import { selectUser } from '../../../../core/stores/user.selectors';
import { User } from '../../../../core/stores/user.model';
import { ClubsService } from '../../services/clubs.service';
import { Club, CLUB_TYPE_STYLES } from '../../models/club.types';
import { ClubPostsService } from '../../services/club-posts.service';
import { ClubPost, POST_TYPE_STYLES } from '../../models/club-post.types';
import { ClubReview } from '../../models/club-review.types';
import { ClubReviewsService } from '../../services/club-reviews.service';
import { ClubMember } from '../../models/club-management.types';
import { ClubManagementService } from '../../services/club-management.service';
import { EventsService } from '../../../events/services/events.service';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { CATEGORY_STYLES, EventItem } from '../../../events/models/event.types';

export type DetailPanel = 'members' | 'events' | 'openEvents' | 'reviews';

@Component({
  selector: 'app-club-detail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './club-detail.component.html',
})
export class ClubDetailComponent implements OnInit, OnDestroy {
  clubId = 0;
  club: Club | null = null;
  loading = true;
  error = '';

  currentUser: User | null = null;
  isMember = false;
  membershipLoading = false;
  joinLeaveLoading = false;
  joinError = '';

  recentPosts: ClubPost[] = [];
  postsLoading = false;

  upcomingEvents: EventItem[] = [];
  eventsLoading = false;

  // Drill-down modal state
  activePanel: DetailPanel | null = null;
  panelLoading = false;
  panelError = '';
  panelPage = 1;
  readonly panelPageSize = 10;
  panelTotalCount = 0;
  panelMembers: ClubMember[] = [];
  panelEvents: EventItem[] = [];
  panelReviews: ClubReview[] = [];

  readonly clubTypeStyles = CLUB_TYPE_STYLES;
  readonly postTypeStyles = POST_TYPE_STYLES;
  readonly categoryStyles = CATEGORY_STYLES;

  private readonly destroy$ = new Subject<void>();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private clubsService: ClubsService,
    private postsService: ClubPostsService,
    private eventsService: EventsService,
    private reviewsService: ClubReviewsService,
    private managementService: ClubManagementService,
    private store: Store,
  ) {}

  ngOnInit(): void {
    this.store
      .select(selectUser)
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        this.currentUser = user;
        if (this.clubId && user) {
          this.fetchMembership();
        } else {
          this.isMember = false;
        }
      });

    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      this.clubId = Number(params.get('clubId')) || 0;
      if (this.clubId) {
        this.fetchClub();
        this.fetchRecentPosts();
        this.fetchUpcomingEvents();
        if (this.currentUser) {
          this.fetchMembership();
        }
      } else {
        this.loading = false;
        this.error = 'Invalid club URL.';
      }
    });
  }

  get isLoggedIn(): boolean {
    return this.currentUser !== null;
  }

  /** A public club can be joined directly; a private one is invite-only. */
  get canJoinDirectly(): boolean {
    return !!this.club && !this.club.isPrivate;
  }

  joinClub(): void {
    if (!this.club || this.joinLeaveLoading) {
      return;
    }
    if (!this.isLoggedIn) {
      void this.router.navigate(['/auth/login'], {
        queryParams: { returnUrl: this.router.url },
      });
      return;
    }

    this.joinLeaveLoading = true;
    this.joinError = '';
    this.clubsService
      .joinClub(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.joinLeaveLoading = false;
          this.isMember = true;
          if (this.club) {
            this.club = { ...this.club, memberCount: this.club.memberCount + 1 };
          }
        },
        error: (err) => {
          this.joinLeaveLoading = false;
          this.joinError = getApiClientMessage(err, 'Unable to join this club.');
        },
      });
  }

  leaveClub(): void {
    if (!this.club || this.joinLeaveLoading) {
      return;
    }

    this.joinLeaveLoading = true;
    this.joinError = '';
    this.clubsService
      .leaveClub(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.joinLeaveLoading = false;
          this.isMember = false;
          if (this.club) {
            this.club = {
              ...this.club,
              memberCount: Math.max(0, this.club.memberCount - 1),
            };
          }
        },
        error: (err) => {
          this.joinLeaveLoading = false;
          this.joinError = getApiClientMessage(err, 'Unable to leave this club.');
        },
      });
  }

  private fetchMembership(): void {
    if (!this.clubId) {
      return;
    }
    this.membershipLoading = true;
    this.clubsService
      .getMembershipStatus(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (isMember) => {
          this.isMember = isMember;
          this.membershipLoading = false;
        },
        // Membership state is best-effort; a failure just hides the "joined" state.
        error: () => {
          this.membershipLoading = false;
        },
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  goBack(): void {
    this.router.navigate(['/clubs']);
  }

  viewPosts(): void {
    this.router.navigate(['/clubs', this.clubId, 'posts']);
  }

  manageClub(): void {
    this.router.navigate(['/clubs', this.clubId, 'manage']);
  }

  // ---- Drill-down modal ----------------------------------------------------

  openPanel(panel: DetailPanel): void {
    this.activePanel = panel;
    this.panelPage = 1;
    this.panelError = '';
    this.panelMembers = [];
    this.panelEvents = [];
    this.panelReviews = [];
    this.panelTotalCount = 0;
    this.loadPanel();
  }

  closePanel(): void {
    this.activePanel = null;
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.activePanel) this.closePanel();
  }

  get panelTotalPages(): number {
    return Math.max(1, Math.ceil(this.panelTotalCount / this.panelPageSize));
  }

  get panelTitle(): string {
    switch (this.activePanel) {
      case 'members':
        return 'Members';
      case 'events':
        return 'All events';
      case 'openEvents':
        return 'Open events';
      case 'reviews':
        return 'Reviews';
      default:
        return '';
    }
  }

  goToPanelPage(page: number): void {
    if (page < 1 || page > this.panelTotalPages || page === this.panelPage) return;
    this.panelPage = page;
    this.loadPanel();
  }

  memberName(member: ClubMember): string {
    return member.name || member.username || `User #${member.userId}`;
  }

  memberInitials(member: ClubMember): string {
    const source = member.name || member.username || '';
    return source ? source.slice(0, 2).toUpperCase() : `#${member.userId}`.slice(0, 2);
  }

  reviewerName(review: ClubReview): string {
    return review.name || review.username || `User #${review.userId}`;
  }

  starsFor(rating: number): number[] {
    return [1, 2, 3, 4, 5].map((n) => (n <= Math.round(rating) ? 1 : 0));
  }

  private loadPanel(): void {
    const panel = this.activePanel;
    if (!panel) return;

    this.panelLoading = true;
    this.panelError = '';

    if (panel === 'reviews') {
      this.reviewsService
        .getReviews(this.clubId, this.panelPage, this.panelPageSize)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (response) => {
            this.panelReviews = response.data?.items ?? [];
            this.panelTotalCount = response.data?.totalCount ?? 0;
            this.panelLoading = false;
          },
          error: (err) => {
            this.panelError = getApiClientMessage(err, 'Unable to load reviews.');
            this.panelLoading = false;
          },
        });
      return;
    }

    if (panel === 'members') {
      this.managementService
        .getMembers(this.clubId, this.panelPage, this.panelPageSize)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (response) => {
            this.panelMembers = response.data?.items ?? [];
            this.panelTotalCount = response.data?.totalCount ?? 0;
            this.panelLoading = false;
          },
          error: (err) => {
            this.panelError = getApiClientMessage(err, 'Unable to load members.');
            this.panelLoading = false;
          },
        });
      return;
    }

    // events | openEvents
    this.eventsService
      .getEventsByClub(this.clubId, {
        status: panel === 'openEvents' ? 'Upcoming' : undefined,
        page: this.panelPage,
        pageSize: this.panelPageSize,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.panelEvents = response.data?.items ?? [];
          this.panelTotalCount = response.data?.totalCount ?? 0;
          this.panelLoading = false;
        },
        error: (err) => {
          this.panelError = getApiClientMessage(err, 'Unable to load events.');
          this.panelLoading = false;
        },
      });
  }

  navigateToPost(post: ClubPost): void {
    this.router.navigate(['/clubs', this.clubId, 'posts', post.id]);
  }

  navigateToEvent(event: EventItem): void {
    this.router.navigate(['/events', event.id]);
  }

  memberCapacityPercent(): number {
    if (!this.club || this.club.maxMemberCount <= 0) return 0;
    return Math.min(100, (this.club.memberCount / this.club.maxMemberCount) * 100);
  }

  registrationPercent(event: EventItem): number {
    if (event.maxParticipants <= 0) return 0;
    return Math.min(100, (event.registrationCount / event.maxParticipants) * 100);
  }

  authorDisplay(post: ClubPost): string {
    return post.author?.name ?? post.author?.username ?? `User #${post.userId}`;
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  formatEventDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
    });
  }

  formatEventTime(iso: string): string {
    return new Date(iso).toLocaleTimeString('en-CA', { hour: '2-digit', minute: '2-digit' });
  }

  formatCost(cost: number): string {
    return cost === 0 ? 'Free' : `$${cost}`;
  }

  private fetchClub(): void {
    this.loading = true;
    this.error = '';

    this.clubsService
      .getClub(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.club = response.data ?? null;
          this.loading = false;
          if (!this.club) this.error = response.message || 'Club not found.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Failed to load club.');
          this.loading = false;
        },
      });
  }

  private fetchRecentPosts(): void {
    this.postsLoading = true;

    this.postsService
      .getPosts(this.clubId, { sortBy: 'Recent', page: 1, pageSize: 3 })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.recentPosts = response.data?.items ?? [];
          this.postsLoading = false;
        },
        error: () => {
          this.postsLoading = false;
        },
      });
  }

  private fetchUpcomingEvents(): void {
    this.eventsLoading = true;

    this.eventsService
      .getEventsByClub(this.clubId, { status: 'Upcoming', page: 1, pageSize: 3 })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.upcomingEvents = response.data?.items ?? [];
          this.eventsLoading = false;
        },
        error: () => {
          this.eventsLoading = false;
        },
      });
  }
}
