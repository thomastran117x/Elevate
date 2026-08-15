import { Component, EventEmitter, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { User } from '../../../../core/stores/user.model';
import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import {
  DiscussionReply,
  DiscussionReplyReaction,
  DiscussionReplySort,
} from '../../models/club-discussion.types';
import { DiscussionRepliesService } from '../../services/discussion-replies.service';
import {
  DiscussionReplyLiveEvent,
  DiscussionReplySseService,
} from '../../services/discussion-reply-sse.service';
import { DiscussionReplyNodeComponent } from '../discussion-reply-node/discussion-reply-node.component';
import {
  applyThreadItem,
  createThreadNode,
  findThreadNode,
  insertThreadNode,
  mergeThreadNodes,
  mergeUniqueThreadNodes,
  ThreadNode,
} from '../thread-tree/thread-tree-state';

export type DiscussionReplyNode = ThreadNode<DiscussionReply>;

export type ReplyNodeAction =
  | { type: 'loadChildren'; node: DiscussionReplyNode; append: boolean }
  | { type: 'create'; node: DiscussionReplyNode; content: string }
  | { type: 'edit'; node: DiscussionReplyNode; content: string }
  | { type: 'delete'; node: DiscussionReplyNode }
  | { type: 'react'; node: DiscussionReplyNode; reaction: DiscussionReplyReaction };

@Component({
  selector: 'app-discussion-reply-thread',
  standalone: true,
  imports: [FormsModule, RouterModule, DiscussionReplyNodeComponent],
  templateUrl: './discussion-reply-thread.component.html',
})
export class DiscussionReplyThreadComponent implements OnInit, OnDestroy {
  @Input({ required: true }) clubId = 0;
  @Input({ required: true }) discussionId = 0;
  @Input() currentUser: User | null = null;
  @Input() initialReplyCount = 0;
  @Output() replyCountChange = new EventEmitter<number>();

  roots: DiscussionReplyNode[] = [];
  sort: DiscussionReplySort = 'Newest';
  nextCursor: string | null = null;
  hasMore = false;
  totalRoots = 0;
  totalReplies = 0;
  loading = false;
  loadingMore = false;
  error = '';
  newReplyText = '';
  submitting = false;

  private readonly seenIds = new Set<number>();
  private readonly destroy$ = new Subject<void>();
  private reconciliationPending = false;

  constructor(
    private repliesService: DiscussionRepliesService,
    private sseService: DiscussionReplySseService,
  ) {}

  ngOnInit(): void {
    this.totalReplies = this.initialReplyCount;
    this.loadRoots(false);
    this.sseService
      .connect(this.clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((event) => this.handleLiveEvent(event));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  changeSort(sort: DiscussionReplySort): void {
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
    this.repliesService
      .getReplies(this.clubId, this.discussionId, null, this.sort, append ? this.nextCursor : null)
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
            if (reconcile) this.adjustTotalReplies(page.totalCount - previousTotalRoots);
            if (!preservePaging) {
              this.nextCursor = page.nextCursor;
              this.hasMore = page.hasMore;
            }
          }
          this.finishRootLoad();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load replies.');
          this.finishRootLoad();
        },
      });
  }

  submitRoot(): void {
    const content = this.newReplyText.trim();
    if (!content || this.submitting) return;
    this.submitting = true;
    this.error = '';
    this.repliesService
      .createReply(this.clubId, this.discussionId, content, null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (response.data) this.registerCreated(response.data);
          this.newReplyText = '';
          this.submitting = false;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to post your reply.');
          this.submitting = false;
        },
      });
  }

  handleNodeAction(action: ReplyNodeAction): void {
    if (action.type === 'loadChildren') this.loadChildren(action.node, action.append);
    if (action.type === 'create') this.createChild(action.node, action.content);
    if (action.type === 'edit') this.editReply(action.node, action.content);
    if (action.type === 'delete') this.deleteReply(action.node);
    if (action.type === 'react') this.toggleReaction(action.node, action.reaction);
  }

  private loadChildren(node: DiscussionReplyNode, append: boolean, refresh = false): void {
    const previousDirectReplyCount = node.directReplyCount;
    const preservePaging = refresh && node.children.length > 0;
    node.loadingChildren = true;
    node.error = '';
    this.repliesService
      .getReplies(
        this.clubId,
        this.discussionId,
        node.id,
        this.sort,
        append ? node.nextCursor : null,
      )
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
              this.adjustTotalReplies(page.totalCount - previousDirectReplyCount);
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

  private createChild(parent: DiscussionReplyNode, content: string): void {
    parent.busy = true;
    parent.error = '';
    this.repliesService
      .createReply(this.clubId, this.discussionId, content, parent.id)
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

  private editReply(node: DiscussionReplyNode, content: string): void {
    node.busy = true;
    node.error = '';
    this.repliesService
      .updateReply(this.clubId, this.discussionId, node.id, content)
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

  private deleteReply(node: DiscussionReplyNode): void {
    node.busy = true;
    node.error = '';
    this.repliesService
      .deleteReply(this.clubId, this.discussionId, node.id)
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

  private toggleReaction(node: DiscussionReplyNode, reaction: DiscussionReplyReaction): void {
    if (!this.currentUser) {
      node.error = 'Sign in to react to replies.';
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
      ? this.repliesService.clearReaction(this.clubId, this.discussionId, node.id)
      : this.repliesService.setReaction(this.clubId, this.discussionId, node.id, reaction);
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

  private handleLiveEvent(event: DiscussionReplyLiveEvent): void {
    if (event.type === 'Connected') {
      if (this.loading || this.loadingMore) {
        this.reconciliationPending = true;
      } else {
        this.reconcileLoadedThread();
      }
      return;
    }
    if (event.type === 'ReplyReactionChanged') {
      if (event.discussionId !== this.discussionId) return;
      const node = findThreadNode(this.roots, event.replyId);
      if (node) {
        node.likeCount = event.likeCount;
        node.dislikeCount = event.dislikeCount;
      }
      return;
    }
    if (event.reply.discussionId !== this.discussionId) return;
    if (event.type === 'ReplyCreated') this.registerCreated(event.reply);
    else this.applyReply(event.reply, event.type === 'ReplyUpdated');
  }

  private registerCreated(reply: DiscussionReply): void {
    if (this.seenIds.has(reply.id)) return;
    this.seenIds.add(reply.id);
    this.totalReplies++;
    this.replyCountChange.emit(this.totalReplies);
    const node = createThreadNode(reply);
    if (reply.parentReplyId === null) {
      this.roots = insertThreadNode(this.roots, node, this.sort);
      this.totalRoots++;
      return;
    }
    const parent = findThreadNode(this.roots, reply.parentReplyId);
    if (!parent) return;
    parent.directReplyCount++;
    if (parent.childrenLoaded) {
      parent.children = insertThreadNode(parent.children, node, this.sort);
    }
  }

  private applyReply(reply: DiscussionReply, preserveViewerReaction = false): void {
    const node = findThreadNode(this.roots, reply.id);
    if (!node) return;
    applyThreadItem(node, reply, preserveViewerReaction);
  }

  private refreshLoadedBranches(nodes: DiscussionReplyNode[]): void {
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

  private adjustTotalReplies(change: number): void {
    if (change === 0) return;
    this.totalReplies = Math.max(0, this.totalReplies + change);
    this.replyCountChange.emit(this.totalReplies);
  }
}
