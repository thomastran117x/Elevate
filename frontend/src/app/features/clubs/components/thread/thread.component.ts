import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { User } from '../../../../core/stores/user.model';
import { ConnectionPillComponent } from '../../../../shared/common/connection-pill/connection-pill.component';
import { PresenceStackComponent } from '../../../../shared/common/presence-stack/presence-stack.component';
import { SkeletonComponent } from '../../../../shared/common/skeleton/skeleton.component';
import { TypingDotsComponent } from '../../../../shared/common/typing-dots/typing-dots.component';
import {
  ClubRealtimeService,
  RealtimeConnectionState,
  RealtimePresenceUser,
} from '../../services/club-realtime.service';
import {
  applyThreadItem,
  createThreadNode,
  findThreadNode,
  insertThreadNode,
  mergeThreadNodes,
  mergeUniqueThreadNodes,
  ThreadSort,
} from '../thread-tree/thread-tree-state';
import { ThreadDataSource, ThreadReaction } from './thread-data-source';
import {
  ThreadDisplayItem,
  ThreadDisplayNode,
  ThreadNodeAction,
  isGroupedWithPrevious,
} from './thread-item';
import { ThreadNodeComponent } from './thread-node.component';

/**
 * A live, chat-style thread. Drives club discussion replies and club post comments from a
 * single implementation via {@link ThreadDataSource}.
 */
@Component({
  selector: 'app-thread',
  standalone: true,
  imports: [
    FormsModule,
    RouterModule,
    ThreadNodeComponent,
    ConnectionPillComponent,
    PresenceStackComponent,
    SkeletonComponent,
    TypingDotsComponent,
  ],
  templateUrl: './thread.component.html',
})
export class ThreadComponent implements OnInit, OnChanges, OnDestroy {
  @Input({ required: true }) source!: ThreadDataSource<ThreadDisplayItem>;
  @Input() currentUser: User | null = null;
  @Input() initialCount = 0;
  @Output() countChange = new EventEmitter<number>();

  roots: ThreadDisplayNode[] = [];
  sort: ThreadSort = 'Newest';
  nextCursor: string | null = null;
  hasMore = false;
  totalRoots = 0;
  totalItems = 0;
  loading = false;
  loadingMore = false;
  error = '';
  newText = '';
  submitting = false;

  connectionState: RealtimeConnectionState = 'connecting';
  presenceUsers: RealtimePresenceUser[] = [];
  totalOnline = 0;
  typingUsers: RealtimePresenceUser[] = [];

  readonly maxLength = 1000;

  private readonly seenIds = new Set<number>();
  private destroy$ = new Subject<void>();
  private reconciliationPending = false;
  /** Composers currently holding content: 'root' plus the id of any open nested reply. */
  private readonly composing = new Set<'root' | number>();
  private leaveThread: (() => void) | null = null;
  private typingActive = false;

  constructor(private realtime: ClubRealtimeService) {}

  ngOnInit(): void {
    this.start();
  }

  ngOnChanges(changes: SimpleChanges): void {
    // The host swaps the source when the user opens a different discussion or post.
    if (changes['source'] && !changes['source'].firstChange) {
      this.teardown();
      this.destroy$ = new Subject<void>();
      this.resetState();
      this.start();
    }
  }

  ngOnDestroy(): void {
    this.teardown();
  }

  // ── Composer ───────────────────────────────────────────────────────────────

  get remainingChars(): number {
    return this.maxLength - this.newText.length;
  }

  /** Only surfaced near the limit, so the counter is not permanent noise. */
  get showCharCount(): boolean {
    return this.remainingChars <= 100;
  }

  get typingLabel(): string {
    const names = this.typingUsers
      .filter((user) => user.userId !== this.currentUser?.Id)
      .map((user) => user.name ?? user.username ?? 'Someone');

    if (names.length === 0) return '';
    if (names.length === 1) return `${names[0]} is typing`;
    if (names.length === 2) return `${names[0]} and ${names[1]} are typing`;
    return 'Several people are typing';
  }

  onComposerInput(): void {
    this.markComposing('root', this.newText.trim().length > 0);
  }

  onComposerKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey) return;
    event.preventDefault();
    this.submitRoot();
  }

  // ── Loading ────────────────────────────────────────────────────────────────

  changeSort(sort: ThreadSort): void {
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

    this.source
      .list(null, this.sort, append ? this.nextCursor : null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (page) => {
          if (page) {
            const incoming = page.items.map(createThreadNode);
            incoming.forEach((node) => this.seenIds.add(node.id));
            this.roots = append
              ? mergeUniqueThreadNodes(this.roots, incoming, this.sort)
              : mergeThreadNodes(this.roots, incoming, this.sort, reconcile);
            this.totalRoots = page.totalCount;
            if (reconcile) this.adjustTotal(page.totalCount - previousTotalRoots);
            if (!preservePaging) {
              this.nextCursor = page.nextCursor;
              this.hasMore = page.hasMore;
            }
          }
          this.finishRootLoad();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, `Unable to load ${this.source.labels.plural}.`);
          this.finishRootLoad();
        },
      });
  }

  submitRoot(): void {
    const content = this.newText.trim();
    if (!content || this.submitting) return;
    this.submitting = true;
    this.error = '';
    this.markComposing('root', false);

    this.source
      .create(content, null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (item) => {
          if (item) this.registerCreated(item);
          this.newText = '';
          this.submitting = false;
        },
        error: (err) => {
          this.error = getApiClientMessage(
            err,
            `Unable to post your ${this.source.labels.singular}.`,
          );
          this.submitting = false;
        },
      });
  }

  isRootGrouped(index: number): boolean {
    return isGroupedWithPrevious(this.roots, index);
  }

  handleNodeAction(action: ThreadNodeAction): void {
    if (action.type === 'loadChildren') this.loadChildren(action.node, action.append);
    if (action.type === 'create') this.createChild(action.node, action.content);
    if (action.type === 'edit') this.editItem(action.node, action.content);
    if (action.type === 'delete') this.deleteItem(action.node);
    if (action.type === 'react') this.toggleReaction(action.node, action.reaction);
    if (action.type === 'typing') this.markComposing(action.node.id, action.active);
  }

  /**
   * Typing is thread-wide, but any of the composers (root or a nested reply) can drive it, so
   * the viewer counts as typing while at least one of them holds content.
   */
  private markComposing(source: 'root' | number, active: boolean): void {
    if (active) this.composing.add(source);
    else this.composing.delete(source);

    this.setTyping(this.composing.size > 0);
  }

  // ── Mutations ──────────────────────────────────────────────────────────────

  private loadChildren(node: ThreadDisplayNode, append: boolean, refresh = false): void {
    const previousDirectReplyCount = node.directReplyCount;
    const preservePaging = refresh && node.children.length > 0;
    node.loadingChildren = true;
    node.error = '';

    this.source
      .list(node.id, this.sort, append ? node.nextCursor : null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (page) => {
          if (page) {
            const incoming = page.items.map(createThreadNode);
            incoming.forEach((child) => this.seenIds.add(child.id));
            node.children = append
              ? mergeUniqueThreadNodes(node.children, incoming, this.sort)
              : mergeThreadNodes(node.children, incoming, this.sort, refresh);
            node.childrenLoaded = true;
            node.directReplyCount = page.totalCount;
            if (refresh) this.adjustTotal(page.totalCount - previousDirectReplyCount);
            if (!preservePaging) {
              node.nextCursor = page.nextCursor;
              node.hasMoreChildren = page.hasMore;
            }
            if (refresh) this.refreshLoadedBranches(node.children);
          }
          node.loadingChildren = false;
        },
        error: (err) => {
          node.error = getApiClientMessage(
            err,
            `Unable to load nested ${this.source.labels.plural}.`,
          );
          node.loadingChildren = false;
        },
      });
  }

  private createChild(parent: ThreadDisplayNode, content: string): void {
    parent.busy = true;
    parent.error = '';
    this.source
      .create(content, parent.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (item) => {
          if (item) this.registerCreated(item);
          parent.replyText = '';
          parent.replyOpen = false;
          parent.busy = false;
          this.markComposing(parent.id, false);
        },
        error: (err) => {
          parent.error = getApiClientMessage(
            err,
            `Unable to post your ${this.source.labels.singular}.`,
          );
          parent.busy = false;
        },
      });
  }

  private editItem(node: ThreadDisplayNode, content: string): void {
    node.busy = true;
    node.error = '';
    this.source
      .update(node.id, content)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (item) => {
          if (item) this.applyItem(item);
          node.editOpen = false;
          node.busy = false;
        },
        error: (err) => {
          node.error = getApiClientMessage(
            err,
            `Unable to edit this ${this.source.labels.singular}.`,
          );
          node.busy = false;
        },
      });
  }

  private deleteItem(node: ThreadDisplayNode): void {
    node.busy = true;
    node.error = '';
    this.source
      .remove(node.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (item) => {
          if (item) this.applyItem(item);
          node.deleteConfirm = false;
          node.busy = false;
        },
        error: (err) => {
          node.error = getApiClientMessage(
            err,
            `Unable to delete this ${this.source.labels.singular}.`,
          );
          node.deleteConfirm = false;
          node.busy = false;
        },
      });
  }

  private toggleReaction(node: ThreadDisplayNode, reaction: ThreadReaction): void {
    if (!this.currentUser) {
      node.error = `Sign in to react to ${this.source.labels.plural}.`;
      return;
    }

    // Optimistic, with a snapshot to roll back to if the request fails.
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
      ? this.source.clearReaction(node.id)
      : this.source.react(node.id, reaction);

    request.pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        if (result) {
          node.likeCount = result.likeCount;
          node.dislikeCount = result.dislikeCount;
          node.currentUserReaction = result.currentUserReaction;
        }
        node.busy = false;
      },
      error: (err) => {
        Object.assign(node, previous);
        node.error = getApiClientMessage(err, 'Unable to update your reaction.');
        node.busy = false;
      },
    });
  }

  // ── Realtime ───────────────────────────────────────────────────────────────

  private start(): void {
    this.totalItems = this.initialCount;
    this.loadRoots(false);

    const { clubId, kind, threadId } = this.source;
    this.leaveThread = this.realtime.joinThread(clubId, kind, threadId);

    this.realtime
      .events(clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((event) => {
        if (event.type === 'Connected') {
          this.onReconnected();
          return;
        }
        const change = this.source.matchLiveEvent(event);
        if (!change) return;

        if (change.kind === 'reaction') {
          const node = findThreadNode(this.roots, change.itemId);
          if (node) {
            node.likeCount = change.likeCount;
            node.dislikeCount = change.dislikeCount;
          }
          return;
        }
        if (change.kind === 'created') this.registerCreated(change.item);
        else this.applyItem(change.item, change.kind === 'updated');
      });

    this.realtime
      .connectionState(clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((state) => (this.connectionState = state));

    this.realtime
      .presence(clubId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((presence) => {
        this.presenceUsers = presence.users;
        this.totalOnline = presence.totalOnline;
      });

    this.realtime
      .typing(clubId, kind, threadId)
      .pipe(takeUntil(this.destroy$))
      .subscribe((users) => (this.typingUsers = users));
  }

  private teardown(): void {
    this.composing.clear();
    this.setTyping(false);
    this.leaveThread?.();
    this.leaveThread = null;
    this.destroy$.next();
    this.destroy$.complete();
  }

  private resetState(): void {
    this.roots = [];
    this.seenIds.clear();
    this.nextCursor = null;
    this.hasMore = false;
    this.totalRoots = 0;
    this.totalItems = 0;
    this.error = '';
    this.newText = '';
    this.typingUsers = [];
    this.reconciliationPending = false;
    this.composing.clear();
  }

  private setTyping(isTyping: boolean): void {
    if (this.typingActive === isTyping) return;
    this.typingActive = isTyping;
    const { clubId, kind, threadId } = this.source;
    this.realtime.setTyping(clubId, kind, threadId, isTyping);
  }

  /**
   * A reconnect may have missed events entirely, so refetch rather than trusting the
   * in-memory tree. Deferred while a load is already in flight to avoid duplicate pages.
   */
  private onReconnected(): void {
    // A rebuilt socket dropped its typing heartbeat, and the local flag would otherwise
    // short-circuit every later keystroke, so re-assert what the composers actually hold.
    this.typingActive = false;
    this.setTyping(this.composing.size > 0);

    if (this.loading || this.loadingMore) {
      this.reconciliationPending = true;
      return;
    }
    this.reconcileLoadedThread();
  }

  // ── Tree bookkeeping ───────────────────────────────────────────────────────

  private registerCreated(item: ThreadDisplayItem): void {
    // The optimistic local insert and the echoed live event collapse into one.
    if (this.seenIds.has(item.id)) return;
    this.seenIds.add(item.id);
    this.totalItems++;
    this.countChange.emit(this.totalItems);

    const node = createThreadNode(item);
    const parentId = this.source.parentIdOf(item);
    if (parentId === null) {
      this.roots = insertThreadNode(this.roots, node, this.sort);
      this.totalRoots++;
      return;
    }

    const parent = findThreadNode(this.roots, parentId);
    if (!parent) return;
    parent.directReplyCount++;
    if (parent.childrenLoaded) {
      parent.children = insertThreadNode(parent.children, node, this.sort);
    }
  }

  private applyItem(item: ThreadDisplayItem, preserveViewerReaction = false): void {
    const node = findThreadNode(this.roots, item.id);
    if (!node) return;
    applyThreadItem(node, item, preserveViewerReaction);
  }

  private refreshLoadedBranches(nodes: ThreadDisplayNode[]): void {
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

  private adjustTotal(change: number): void {
    if (change === 0) return;
    this.totalItems = Math.max(0, this.totalItems + change);
    this.countChange.emit(this.totalItems);
  }
}
