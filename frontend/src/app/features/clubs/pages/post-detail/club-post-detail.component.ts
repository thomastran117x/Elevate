import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { ClubPostsService } from '../../services/club-posts.service';
import { ClubPost } from '../../models/club-post.types';
import { CommentThreadComponent } from '../../components/comment-thread/comment-thread.component';

@Component({
  selector: 'app-club-post-detail',
  standalone: true,
  imports: [CommonModule, CommentThreadComponent],
  templateUrl: './club-post-detail.component.html',
})
export class ClubPostDetailComponent implements OnInit, OnDestroy {
  clubId = 0;
  postId = 0;
  post: ClubPost | null = null;
  loading = true;
  error = '';

  private readonly destroy$ = new Subject<void>();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private postsService: ClubPostsService,
  ) {}

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      this.clubId = Number(params.get('clubId')) || 0;
      this.postId = Number(params.get('postId')) || 0;
      if (this.clubId && this.postId) {
        this.fetch();
      } else {
        this.loading = false;
        this.error = 'Invalid post URL.';
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  goBack(): void {
    this.router.navigate(['/clubs', this.clubId, 'posts']);
  }

  authorDisplay(post: ClubPost): string {
    return post.author?.name ?? post.author?.username ?? `User #${post.userId}`;
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  private fetch(): void {
    this.loading = true;
    this.error = '';

    this.postsService
      .getPost(this.clubId, this.postId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const data = response.data ?? null;
          this.post = data;
          this.loading = false;
          if (!data) {
            this.error = response.message || 'Post not found.';
          }
        },
        error: (err) => {
          this.error =
            err?.error?.message || err?.error?.Message || 'Failed to load post.';
          this.loading = false;
        },
      });
  }
}
