import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { ClubsService } from '../../services/clubs.service';
import { Club, CLUB_TYPE_STYLES } from '../../models/club.types';
import { ClubPostsService } from '../../services/club-posts.service';
import { ClubPost, POST_TYPE_STYLES } from '../../models/club-post.types';
import { EventsService } from '../../../events/services/events.service';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { CATEGORY_STYLES, EventItem } from '../../../events/models/event.types';

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

  recentPosts: ClubPost[] = [];
  postsLoading = false;

  upcomingEvents: EventItem[] = [];
  eventsLoading = false;

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
  ) {}

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      this.clubId = Number(params.get('clubId')) || 0;
      if (this.clubId) {
        this.fetchClub();
        this.fetchRecentPosts();
        this.fetchUpcomingEvents();
      } else {
        this.loading = false;
        this.error = 'Invalid club URL.';
      }
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
