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

export interface DiscussionReplyNode extends DiscussionReply {
  children: DiscussionReplyNode[];
  childrenLoaded: boolean;
  loadingChildren: boolean;
  nextCursor: string | null;
  hasMoreChildren: boolean;
  replyOpen: boolean;
  replyText: string;
  editOpen: boolean;
  editText: string;
  deleteConfirm: boolean;
  busy: boolean;
  error: string;
}

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

  loadRoots(append: boolean): void {
    append ? (this.loadingMore = true) : (this.loading = true);
    this.repliesService
      .getReplies(this.clubId, this.discussionId, null, this.sort, append ? this.nextCursor : null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          const page = response.data;
          if (page) {
            const incoming = page.items.map((reply) => this.toNode(reply));
            incoming.forEach((node) => this.seenIds.add(node.id));
            this.roots = append
              ? this.mergeUnique(this.roots, incoming)
              : this.mergeNodes(this.roots, incoming);
            this.totalRoots = page.totalCount;
            this.nextCursor = page.nextCursor;
            this.hasMore = page.hasMore;
          }
          this.loading = false;
          this.loadingMore = false;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load replies.');
          this.loading = false;
          this.loadingMore = false;
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
            const incoming = page.items.map((reply) => this.toNode(reply));
            incoming.forEach((child) => this.seenIds.add(child.id));
            node.children = append
              ? this.mergeUnique(node.children, incoming)
              : this.mergeNodes(node.children, incoming);
            node.childrenLoaded = true;
            node.directReplyCount = page.totalCount;
            node.nextCursor = page.nextCursor;
            node.hasMoreChildren = page.hasMore;
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
      if (!this.loading && this.roots.length > 0) {
        this.loadRoots(false);
        this.refreshLoadedBranches(this.roots);
      }
      return;
    }
    if (event.type === 'ReplyReactionChanged') {
      if (event.discussionId !== this.discussionId) return;
      const node = this.findNode(event.replyId);
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
    const node = this.toNode(reply);
    if (reply.parentReplyId === null) {
      this.roots = this.insertBySort(this.roots, node);
      this.totalRoots++;
      return;
    }
    const parent = this.findNode(reply.parentReplyId);
    if (!parent) return;
    parent.directReplyCount++;
    if (parent.childrenLoaded) parent.children = this.insertBySort(parent.children, node);
  }

  private applyReply(reply: DiscussionReply, preserveViewerReaction = false): void {
    const node = this.findNode(reply.id);
    if (!node) return;
    const currentUserReaction = node.currentUserReaction;
    const state = {
      children: node.children,
      childrenLoaded: node.childrenLoaded,
      loadingChildren: node.loadingChildren,
      nextCursor: node.nextCursor,
      hasMoreChildren: node.hasMoreChildren,
      replyOpen: node.replyOpen,
      replyText: node.replyText,
      editOpen: node.editOpen,
      editText: node.editText,
      deleteConfirm: node.deleteConfirm,
      busy: node.busy,
      error: node.error,
    };
    Object.assign(node, reply, state);
    if (preserveViewerReaction) node.currentUserReaction = currentUserReaction;
  }

  private refreshLoadedBranches(nodes: DiscussionReplyNode[]): void {
    for (const node of nodes) {
      if (node.childrenLoaded) this.loadChildren(node, false, true);
    }
  }

  private findNode(id: number, nodes = this.roots): DiscussionReplyNode | null {
    for (const node of nodes) {
      if (node.id === id) return node;
      const found = this.findNode(id, node.children);
      if (found) return found;
    }
    return null;
  }

  private mergeNodes(
    existing: DiscussionReplyNode[],
    incoming: DiscussionReplyNode[],
  ): DiscussionReplyNode[] {
    const byId = new Map(existing.map((node) => [node.id, node]));
    return incoming.map((node) => {
      const prior = byId.get(node.id);
      if (!prior) return node;
      const state = {
        children: prior.children,
        childrenLoaded: prior.childrenLoaded,
        loadingChildren: prior.loadingChildren,
        nextCursor: prior.nextCursor,
        hasMoreChildren: prior.hasMoreChildren,
        replyOpen: prior.replyOpen,
        replyText: prior.replyText,
        editOpen: prior.editOpen,
        editText: prior.editText,
        deleteConfirm: prior.deleteConfirm,
        busy: prior.busy,
        error: prior.error,
      };
      return Object.assign(prior, node, state);
    });
  }

  private mergeUnique(
    current: DiscussionReplyNode[],
    incoming: DiscussionReplyNode[],
  ): DiscussionReplyNode[] {
    const byId = new Map(current.map((node) => [node.id, node]));
    for (const node of incoming) {
      if (!byId.has(node.id)) byId.set(node.id, node);
    }
    return [...byId.values()].sort((left, right) => this.compareBySort(left, right));
  }

  private insertBySort(
    current: DiscussionReplyNode[],
    node: DiscussionReplyNode,
  ): DiscussionReplyNode[] {
    return this.mergeUnique(current, [node]);
  }

  private compareBySort(left: DiscussionReplyNode, right: DiscussionReplyNode): number {
    const createdAtDifference =
      new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime();
    const ascending = createdAtDifference || left.id - right.id;
    return this.sort === 'Newest' ? -ascending : ascending;
  }

  private toNode(reply: DiscussionReply): DiscussionReplyNode {
    return {
      ...reply,
      children: [],
      childrenLoaded: false,
      loadingChildren: false,
      nextCursor: null,
      hasMoreChildren: false,
      replyOpen: false,
      replyText: '',
      editOpen: false,
      editText: '',
      deleteConfirm: false,
      busy: false,
      error: '',
    };
  }
}
