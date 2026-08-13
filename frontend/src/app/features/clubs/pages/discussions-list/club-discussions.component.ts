import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Store } from '@ngrx/store';
import { Subject, finalize, takeUntil } from 'rxjs';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { User } from '../../../../core/stores/user.model';
import { selectUser } from '../../../../core/stores/user.selectors';
import { ClubDiscussionsService } from '../../services/club-discussions.service';
import { ClubsService } from '../../services/clubs.service';
import { ClubDiscussion, discussionAuthorName } from '../../models/club-discussion.types';
import { Club } from '../../models/club.types';

/** Frontend mirror of the server's `HasClubStaffAccessAsync`: owner, staff, or admin. */
function isClubStaff(club: Club | null): boolean {
  return !!club && (club.isOwner || club.isManager || club.isVolunteer || club.canManage);
}

@Component({
  selector: 'app-club-discussions',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './club-discussions.component.html',
})
export class ClubDiscussionsComponent implements OnInit, OnDestroy {
  readonly titleMax = 150;
  readonly descriptionMax = 2000;
  readonly pageSize = 20;

  clubId = 0;
  discussions: ClubDiscussion[] = [];
  totalCount = 0;
  totalPages = 0;
  currentPage = 1;

  loading = false;
  loadingMore = false;
  error = '';
  success = '';

  currentUser: User | null = null;
  isMember = false;
  isStaff = false;

  showEditor = false;
  editingId: number | null = null;
  saving = false;
  form = { title: '', description: '' };

  private readonly destroy$ = new Subject<void>();
  private requestVersion = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private store: Store,
    private discussionsService: ClubDiscussionsService,
    private clubsService: ClubsService,
  ) {}

  ngOnInit(): void {
    this.store
      .select(selectUser)
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        this.currentUser = user;
        if (this.clubId) this.fetchAccess();
      });

    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = Number(params.get('clubId'));
      this.clubId = id > 0 ? id : 0;
      this.fetchAccess();
      this.resetAndFetch();
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Mirrors the server's `IsMemberOrStaffAsync`: members may start a discussion, and so
   * may owners/staff/admins. Staff are checked separately because they have no
   * `FollowClub` row — the owner of a private club in particular can never join through
   * the normal flow, so membership alone would hide the control from them.
   */
  get canStartDiscussion(): boolean {
    return !!this.currentUser && (this.isMember || this.isStaff);
  }

  isAuthor(discussion: ClubDiscussion): boolean {
    return !!this.currentUser && discussion.userId === this.currentUser.Id;
  }

  authorDisplay(discussion: ClubDiscussion): string {
    return discussionAuthorName(discussion);
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  goBack(): void {
    this.router.navigate(['/clubs', this.clubId]);
  }

  openCreate(): void {
    this.editingId = null;
    this.form = { title: '', description: '' };
    this.error = '';
    this.success = '';
    this.showEditor = true;
  }

  openEdit(discussion: ClubDiscussion): void {
    this.editingId = discussion.id;
    this.form = { title: discussion.title, description: discussion.description };
    this.error = '';
    this.success = '';
    this.showEditor = true;
  }

  cancelEditor(): void {
    this.showEditor = false;
    this.editingId = null;
    this.form = { title: '', description: '' };
  }

  saveDiscussion(): void {
    const title = this.form.title.trim();
    const description = this.form.description.trim();

    if (!title || !description) {
      this.error = 'Title and description are required.';
      return;
    }

    this.saving = true;
    this.error = '';

    const request$ =
      this.editingId != null
        ? this.discussionsService.updateDiscussion(this.clubId, this.editingId, {
            title,
            description,
          })
        : this.discussionsService.createDiscussion(this.clubId, { title, description });

    const wasEditing = this.editingId != null;

    request$
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => (this.saving = false)),
      )
      .subscribe({
        next: () => {
          this.success = wasEditing ? 'Discussion updated.' : 'Discussion started.';
          this.cancelEditor();
          this.resetAndFetch();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to save the discussion.');
        },
      });
  }

  deleteDiscussion(discussion: ClubDiscussion): void {
    this.error = '';
    this.success = '';

    this.discussionsService
      .deleteDiscussion(this.clubId, discussion.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.success = 'Discussion deleted.';
          this.resetAndFetch();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to delete the discussion.');
        },
      });
  }

  loadMore(): void {
    if (this.currentPage >= this.totalPages || this.loadingMore) return;
    this.currentPage++;
    this.fetch(true);
  }

  private fetchAccess(): void {
    if (!this.clubId || !this.currentUser) {
      this.isMember = false;
      this.isStaff = false;
      return;
    }

    this.clubsService
      .getMembershipStatus(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (isMember) => (this.isMember = isMember),
        error: () => (this.isMember = false),
      });

    // `canManage` also covers admins, who carry no per-club owner/staff flag.
    this.clubsService
      .getClub(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => (this.isStaff = isClubStaff(response.data ?? null)),
        error: () => (this.isStaff = false),
      });
  }

  private resetAndFetch(): void {
    this.currentPage = 1;
    this.discussions = [];
    this.fetch(false);
  }

  private fetch(append: boolean): void {
    if (!this.clubId) return;

    const version = ++this.requestVersion;
    if (append) {
      this.loadingMore = true;
    } else {
      this.loading = true;
      this.error = '';
    }

    this.discussionsService
      .getDiscussions(this.clubId, this.currentPage, this.pageSize)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (version !== this.requestVersion) return;
          const data = response.data ?? null;
          if (data) {
            this.discussions = append ? [...this.discussions, ...data.items] : data.items;
            this.totalCount = data.totalCount;
            this.totalPages = data.totalPages || Math.ceil(data.totalCount / this.pageSize);
          }
          this.loading = false;
          this.loadingMore = false;
        },
        error: (err) => {
          if (version !== this.requestVersion) return;
          // Roll the page back so a retry re-requests the page that failed instead of
          // skipping it and leaving a hole in the accumulated list.
          if (append) this.currentPage--;
          this.error = getApiClientMessage(err, 'Failed to load discussions.');
          this.loading = false;
          this.loadingMore = false;
        },
      });
  }
}
