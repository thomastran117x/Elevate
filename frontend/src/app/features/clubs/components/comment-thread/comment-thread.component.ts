import { Component, EventEmitter, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { User } from '../../../../core/stores/user.model';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { PostComment, PostCommentReaction, PostCommentSort } from '../../models/club-post.types';
import { PostCommentsService } from '../../services/post-comments.service';
import { SseCommentEvent, CommentSseService } from '../../services/comment-sse.service';
import { PostCommentNodeComponent } from '../post-comment-node/post-comment-node.component';
import {
  applyThreadItem,
  createThreadNode,
  findThreadNode,
  insertThreadNode,
  mergeThreadNodes,
  mergeUniqueThreadNodes,
  ThreadNode,
} from '../thread-tree/thread-tree-state';

export type PostCommentNode = ThreadNode<PostComment>;

export type CommentNodeAction =
  | { type: 'loadChildren'; node: PostCommentNode; append: boolean }
  | { type: 'create'; node: PostCommentNode; content: string }
  | { type: 'edit'; node: PostCommentNode; content: string }
  | { type: 'delete'; node: PostCommentNode }
  | { type: 'react'; node: PostCommentNode; reaction: PostCommentReaction };

@Component({
  selector: 'app-comment-thread',
  standalone: true,
  imports: [FormsModule, RouterModule, PostCommentNodeComponent],
  templateUrl: './comment-thread.component.html',
})
export class CommentThreadComponent implements OnInit, OnDestroy {
  @Input({ required: true }) clubId = 0;
  @Input({ required: true }) postId = 0;
  @Input() currentUser: User | null = null;
  @Input() initialCommentCount = 0;
  @Output() commentCountChange = new EventEmitter<number>();

  roots: PostCommentNode[] = [];
  sort: PostCommentSort = 'Newest';
  nextCursor: string | null = null;
  hasMore = false;
  totalRoots = 0;
  totalComments = 0;
  loading = false;
  loadingMore = false;
  error = '';
  newCommentText = '';
  submitting = false;

  private readonly seenIds = new Set<number>();
  private readonly destroy$ = new Subject<void>();
  private reconciliationPending = false;

  constructor(
    private commentsService: PostCommentsService,
    private commentSseService: CommentSseService,
  ) {}

  ngOnInit(): void {
    this.totalComments = this.initialCommentCount;
    this.loadRoots(false);
    this.commentSseService
      .connect(this.clubId, this.postId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((event) => this.handleLiveEvent(event));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  changeSort(sort: PostCommentSort): void {
    if (sort === this.sort) return;
    this.sort = sort;
    this.roots = [];
    this.seenIds.clear();
    this.nextCursor = null;
    this.hasMore = false;
    this.loadRoots(false);
  }

  loadRoots(append: boolean, reconcile = false): void {
    const previousTotalRoots = this.totalRoots;
    const preservePaging = reconcile && this.roots.length > 0;
    append ? (this.loadingMore = true) : (this.loading = true);
    this.commentsService
      .getComments(this.clubId, this.postId, null, this.sort, append ? this.nextCursor : null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const page = response.data;
          if (page) {
            const incoming = page.items.map(createThreadNode);
            incoming.forEach((node) => this.seenIds.add(node.id));
            this.roots = append
              ? mergeUniqueThreadNodes(this.roots, incoming, this.sort)
              : mergeThreadNodes(this.roots, incoming, this.sort, reconcile);
            this.totalRoots = page.totalCount;
            if (reconcile) this.adjustTotalComments(page.totalCount - previousTotalRoots);
            if (!preservePaging) {
              this.nextCursor = page.nextCursor;
              this.hasMore = page.hasMore;
            }
          }
          this.finishRootLoad();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load comments.');
          this.finishRootLoad();
        },
      });
  }

  submitRoot(): void {
    const content = this.newCommentText.trim();
    if (!content || this.submitting) return;
    this.submitting = true;
    this.error = '';
    this.commentsService
      .createComment(this.clubId, this.postId, content, null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (response.data) this.registerCreated(response.data);
          this.newCommentText = '';
          this.submitting = false;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to post your comment.');
          this.submitting = false;
        },
      });
  }

  handleNodeAction(action: CommentNodeAction): void {
    if (action.type === 'loadChildren') this.loadChildren(action.node, action.append);
    if (action.type === 'create') this.createChild(action.node, action.content);
    if (action.type === 'edit') this.editReply(action.node, action.content);
    if (action.type === 'delete') this.deleteComment(action.node);
    if (action.type === 'react') this.toggleReaction(action.node, action.reaction);
  }

  private loadChildren(node: PostCommentNode, append: boolean, refresh = false): void {
    const previousDirectReplyCount = node.directReplyCount;
    const preservePaging = refresh && node.children.length > 0;
    node.loadingChildren = true;
    node.error = '';
    this.commentsService
      .getComments(this.clubId, this.postId, node.id, this.sort, append ? node.nextCursor : null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const page = response.data;
          if (page) {
            const incoming = page.items.map(createThreadNode);
            incoming.forEach((child) => this.seenIds.add(child.id));
            node.children = append
              ? mergeUniqueThreadNodes(node.children, incoming, this.sort)
              : mergeThreadNodes(node.children, incoming, this.sort, refresh);
            node.childrenLoaded = true;
            node.directReplyCount = page.totalCount;
            if (refresh) {
              this.adjustTotalComments(page.totalCount - previousDirectReplyCount);
            }
            if (!preservePaging) {
              node.nextCursor = page.nextCursor;
              node.hasMoreChildren = page.hasMore;
            }
            if (refresh) this.refreshLoadedBranches(node.children);
          }
          node.loadingChildren = false;
        },
        error: (err) => {
          node.error = getApiClientMessage(err, 'Unable to load nested replies.');
          node.loadingChildren = false;
        },
      });
  }

  private createChild(parent: PostCommentNode, content: string): void {
    parent.busy = true;
    parent.error = '';
    this.commentsService
      .createComment(this.clubId, this.postId, content, parent.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (response.data) this.registerCreated(response.data);
          parent.replyText = '';
          parent.replyOpen = false;
          parent.busy = false;
        },
        error: (err) => {
          parent.error = getApiClientMessage(err, 'Unable to post your reply.');
          parent.busy = false;
        },
      });
  }

  private editReply(node: PostCommentNode, content: string): void {
    node.busy = true;
    node.error = '';
    this.commentsService
      .updateComment(this.clubId, this.postId, node.id, content)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (response.data) this.applyReply(response.data);
          node.editOpen = false;
          node.busy = false;
        },
        error: (err) => {
          node.error = getApiClientMessage(err, 'Unable to edit this reply.');
          node.busy = false;
        },
      });
  }

  private deleteComment(node: PostCommentNode): void {
    node.busy = true;
    node.error = '';
    this.commentsService
      .deleteComment(this.clubId, this.postId, node.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (response.data) this.applyReply(response.data);
          node.deleteConfirm = false;
          node.busy = false;
        },
        error: (err) => {
          node.error = getApiClientMessage(err, 'Unable to delete this reply.');
          node.deleteConfirm = false;
          node.busy = false;
        },
      });
  }

  private toggleReaction(node: PostCommentNode, reaction: PostCommentReaction): void {
    if (!this.currentUser) {
      node.error = 'Sign in to react to comments.';
      return;
    }
    const previous = {
      likeCount: node.likeCount,
      dislikeCount: node.dislikeCount,
      currentUserReaction: node.currentUserReaction,
    };
    const clearing = node.currentUserReaction === reaction;
    if (node.currentUserReaction === 'Like') node.likeCount--;
    if (node.currentUserReaction === 'Dislike') node.dislikeCount--;
    if (!clearing && reaction === 'Like') node.likeCount++;
    if (!clearing && reaction === 'Dislike') node.dislikeCount++;
    node.currentUserReaction = clearing ? null : reaction;
    node.busy = true;

    const request = clearing
      ? this.commentsService.clearReaction(this.clubId, this.postId, node.id)
      : this.commentsService.setReaction(this.clubId, this.postId, node.id, reaction);
    request.pipe(takeUntil(this.destroy$)).subscribe({
      next: (response) => {
        if (response.data) Object.assign(node, response.data);
        node.busy = false;
      },
      error: (err) => {
        Object.assign(node, previous);
        node.error = getApiClientMessage(err, 'Unable to update your reaction.');
        node.busy = false;
      },
    });
  }

  private handleLiveEvent(event: SseCommentEvent): void {
    if (event.type === 'Connected') {
      if (this.loading || this.loadingMore) {
        this.reconciliationPending = true;
      } else {
        this.reconcileLoadedThread();
      }
      return;
    }
    if (event.type === 'CommentReactionChanged') {
      if (event.postId !== this.postId) return;
      const node = findThreadNode(this.roots, event.commentId);
      if (node) {
        node.likeCount = event.likeCount;
        node.dislikeCount = event.dislikeCount;
      }
      return;
    }
    if (event.comment.postId !== this.postId) return;
    if (event.type === 'CommentCreated') this.registerCreated(event.comment);
    else this.applyReply(event.comment, event.type === 'CommentUpdated');
  }

  private registerCreated(reply: PostComment): void {
    if (this.seenIds.has(reply.id)) return;
    this.seenIds.add(reply.id);
    this.totalComments++;
    this.commentCountChange.emit(this.totalComments);
    const node = createThreadNode(reply);
    if (reply.parentCommentId === null) {
      this.roots = insertThreadNode(this.roots, node, this.sort);
      this.totalRoots++;
      return;
    }
    const parent = findThreadNode(this.roots, reply.parentCommentId);
    if (!parent) return;
    parent.directReplyCount++;
    if (parent.childrenLoaded) {
      parent.children = insertThreadNode(parent.children, node, this.sort);
    }
  }

  private applyReply(reply: PostComment, preserveViewerReaction = false): void {
    const node = findThreadNode(this.roots, reply.id);
    if (!node) return;
    applyThreadItem(node, reply, preserveViewerReaction);
  }

  private refreshLoadedBranches(nodes: PostCommentNode[]): void {
    for (const node of nodes) {
      if (node.childrenLoaded) this.loadChildren(node, false, true);
    }
  }

  private reconcileLoadedThread(): void {
    this.loadRoots(false, true);
    this.refreshLoadedBranches(this.roots);
  }

  private finishRootLoad(): void {
    this.loading = false;
    this.loadingMore = false;
    if (!this.reconciliationPending) return;
    this.reconciliationPending = false;
    this.reconcileLoadedThread();
  }

  private adjustTotalComments(change: number): void {
    if (change === 0) return;
    this.totalComments = Math.max(0, this.totalComments + change);
    this.commentCountChange.emit(this.totalComments);
  }
}
