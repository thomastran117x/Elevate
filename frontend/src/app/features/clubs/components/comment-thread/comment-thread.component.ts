import { Component, Input, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Store } from '@ngrx/store';
import { Subject, takeUntil } from 'rxjs';

import { PostCommentsService } from '../../services/post-comments.service';
import { CommentSseService } from '../../services/comment-sse.service';
import { PostComment } from '../../models/club-post.types';
import { selectUser } from '../../../../core/stores/user.selectors';
import { User } from '../../../../core/stores/user.model';

@Component({
  selector: 'app-comment-thread',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './comment-thread.component.html',
})
export class CommentThreadComponent implements OnInit, OnDestroy {
  @Input() clubId = 0;
  @Input() postId = 0;

  comments: PostComment[] = [];
  totalCount = 0;
  currentPage = 1;
  totalPages = 0;
  readonly pageSize = 20;

  loading = false;
  loadingMore = false;
  submitting = false;
  loadError = '';
  submitError = '';

  newCommentText = '';
  editingId: number | null = null;
  editText = '';
  deletingId: number | null = null;

  currentUser: User | null = null;

  private readonly destroy$ = new Subject<void>();

  constructor(
    private commentsService: PostCommentsService,
    private sseService: CommentSseService,
    private store: Store,
  ) {}

  ngOnInit(): void {
    this.store
      .select(selectUser)
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        this.currentUser = user;
      });
    this.loadComments(false);
    this.connectSse();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get isLoggedIn(): boolean {
    return this.currentUser !== null;
  }

  isOwnComment(comment: PostComment): boolean {
    return this.currentUser !== null && comment.userId === this.currentUser.Id;
  }

  authorDisplay(comment: PostComment): string {
    return comment.author?.name ?? comment.author?.username ?? `User #${comment.userId}`;
  }

  authorInitials(comment: PostComment): string {
    const name = comment.author?.name ?? comment.author?.username ?? '';
    return (
      name
        .split(' ')
        .slice(0, 2)
        .map((w) => w[0]?.toUpperCase() ?? '')
        .join('') || '?'
    );
  }

  formatDate(iso: string): string {
    const d = new Date(iso);
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    const diffMin = Math.floor(diffMs / 60000);
    if (diffMin < 1) return 'just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    const diffDays = Math.floor(diffHr / 24);
    if (diffDays < 7) return `${diffDays}d ago`;
    return d.toLocaleDateString('en-CA', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  loadMore(): void {
    if (this.loadingMore || this.currentPage >= this.totalPages) return;
    this.currentPage++;
    this.loadComments(true);
  }

  submitComment(): void {
    const content = this.newCommentText.trim();
    if (!content || this.submitting) return;

    this.submitting = true;
    this.submitError = '';

    this.commentsService
      .createComment(this.clubId, this.postId, content)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const comment = response.data;
          if (comment) {
            this.comments = [comment, ...this.comments];
            this.totalCount++;
          }
          this.newCommentText = '';
          this.submitting = false;
        },
        error: (err) => {
          this.submitError =
            err?.error?.message || err?.error?.Message || 'Failed to post comment.';
          this.submitting = false;
        },
      });
  }

  startEdit(comment: PostComment): void {
    this.editingId = comment.id;
    this.editText = comment.content;
  }

  cancelEdit(): void {
    this.editingId = null;
    this.editText = '';
  }

  saveEdit(comment: PostComment): void {
    const content = this.editText.trim();
    if (!content || content === comment.content) {
      this.cancelEdit();
      return;
    }

    this.commentsService
      .updateComment(this.clubId, this.postId, comment.id, content)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const updated = response.data;
          if (updated) {
            this.comments = this.comments.map((c) => (c.id === updated.id ? updated : c));
          }
          this.cancelEdit();
        },
        error: (err) => {
          this.submitError =
            err?.error?.message || err?.error?.Message || 'Failed to update comment.';
        },
      });
  }

  confirmDelete(commentId: number): void {
    this.deletingId = commentId;
  }

  cancelDelete(): void {
    this.deletingId = null;
  }

  deleteComment(commentId: number): void {
    this.commentsService
      .deleteComment(this.clubId, this.postId, commentId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.comments = this.comments.filter((c) => c.id !== commentId);
          this.totalCount = Math.max(0, this.totalCount - 1);
          this.deletingId = null;
        },
        error: (err) => {
          this.submitError =
            err?.error?.message || err?.error?.Message || 'Failed to delete comment.';
          this.deletingId = null;
        },
      });
  }

  private connectSse(): void {
    this.sseService
      .connect(this.clubId, this.postId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((event) => {
        if (event.type === 'CommentCreated') {
          if (!this.comments.some((c) => c.id === event.comment.id)) {
            this.comments = [event.comment, ...this.comments];
            this.totalCount++;
          }
        } else if (event.type === 'CommentUpdated') {
          this.comments = this.comments.map((c) => (c.id === event.comment.id ? event.comment : c));
        } else if (event.type === 'CommentDeleted') {
          if (this.comments.some((c) => c.id === event.commentId)) {
            this.comments = this.comments.filter((c) => c.id !== event.commentId);
            this.totalCount = Math.max(0, this.totalCount - 1);
          }
        }
      });
  }

  private loadComments(append: boolean): void {
    if (append) {
      this.loadingMore = true;
    } else {
      this.loading = true;
      this.loadError = '';
    }

    this.commentsService
      .getComments(this.clubId, this.postId, this.currentPage, this.pageSize)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const data = response.data;
          if (data) {
            this.comments = append ? [...this.comments, ...data.items] : data.items;
            this.totalCount = data.totalCount;
            this.totalPages = data.totalPages || Math.ceil(data.totalCount / this.pageSize);
          }
          this.loading = false;
          this.loadingMore = false;
        },
        error: (err) => {
          this.loadError = err?.error?.message || err?.error?.Message || 'Failed to load comments.';
          this.loading = false;
          this.loadingMore = false;
        },
      });
  }
}
