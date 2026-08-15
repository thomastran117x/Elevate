import { of, Subject, throwError } from 'rxjs';

import {
  DiscussionReply,
  DiscussionReplyApiResponse,
  DiscussionReplyPageApiResponse,
  DiscussionReplyReactionApiResponse,
} from '../../models/club-discussion.types';
import { DiscussionRepliesService } from '../../services/discussion-replies.service';
import {
  DiscussionReplyLiveEvent,
  DiscussionReplySseService,
} from '../../services/discussion-reply-sse.service';
import { DiscussionReplyThreadComponent } from './discussion-reply-thread.component';

describe('DiscussionReplyThreadComponent', () => {
  let replies: jasmine.SpyObj<DiscussionRepliesService>;
  let live: Subject<DiscussionReplyLiveEvent>;
  let component: DiscussionReplyThreadComponent;

  beforeEach(() => {
    replies = jasmine.createSpyObj<DiscussionRepliesService>('DiscussionRepliesService', [
      'getReplies',
      'createReply',
      'updateReply',
      'deleteReply',
      'setReaction',
      'clearReaction',
    ]);
    live = new Subject<DiscussionReplyLiveEvent>();
    const sse = {
      connect: () => live.asObservable(),
    } as unknown as DiscussionReplySseService;
    replies.getReplies.and.returnValue(of(page([])));
    component = new DiscussionReplyThreadComponent(replies, sse);
    component.clubId = 4;
    component.discussionId = 9;
  });

  afterEach(() => component.ngOnDestroy());

  it('loads children lazily at the selected sort level', () => {
    const root = reply(1, null, 1);
    replies.getReplies.and.returnValues(of(page([root])), of(page([reply(2, 1)])));
    component.ngOnInit();

    component.handleNodeAction({ type: 'loadChildren', node: component.roots[0], append: false });

    expect(replies.getReplies.calls.mostRecent().args).toEqual([4, 9, 1, 'Newest', null]);
    expect(component.roots[0].children.map((item) => item.id)).toEqual([2]);
  });

  it('keeps live replies correctly ordered and deduplicated across Oldest pages', () => {
    component.sort = 'Oldest';
    replies.getReplies.and.returnValues(
      of(page([reply(1, null, 0, '2026-08-14T01:00:00Z')], true, 'next')),
      of(
        page([
          reply(2, null, 0, '2026-08-14T02:00:00Z'),
          reply(3, null, 0, '2026-08-14T03:00:00Z'),
        ]),
      ),
    );
    component.initialReplyCount = 2;
    component.ngOnInit();
    live.next({ type: 'ReplyCreated', reply: reply(3, null, 0, '2026-08-14T03:00:00Z') });

    component.loadRoots(true);

    expect(component.roots.map((item) => item.id)).toEqual([1, 2, 3]);
    expect(component.totalReplies).toBe(3);
  });

  it('reloads roots and expanded branches when the stream reconnects', () => {
    const root = reply(1, null, 1);
    replies.getReplies.and.returnValue(of(page([root])));
    component.ngOnInit();
    component.roots[0].childrenLoaded = true;

    live.next({ type: 'Connected' });

    expect(replies.getReplies.calls.allArgs()).toContain([4, 9, null, 'Newest', null]);
    expect(replies.getReplies.calls.allArgs()).toContain([4, 9, 1, 'Newest', null]);
  });

  it('rolls back an optimistic reaction when the API request fails', () => {
    const root = reply(1);
    replies.getReplies.and.returnValue(of(page([root])));
    replies.setReaction.and.returnValue(throwError(() => new Error('offline')));
    component.currentUser = { Id: 7 } as never;
    component.ngOnInit();

    component.handleNodeAction({ type: 'react', node: component.roots[0], reaction: 'Like' });

    expect(component.roots[0].likeCount).toBe(0);
    expect(component.roots[0].currentUserReaction).toBeNull();
    expect(component.roots[0].error).toBe('offline');
  });

  it('resets cached pages when the sort changes and ignores the selected sort', () => {
    replies.getReplies.and.returnValue(of(page([reply(1)], true, 'next')));
    component.ngOnInit();

    component.changeSort('Newest');
    expect(replies.getReplies).toHaveBeenCalledTimes(1);

    component.changeSort('Oldest');

    expect(replies.getReplies.calls.mostRecent().args).toEqual([4, 9, null, 'Oldest', null]);
    expect(component.nextCursor).toBe('next');
    expect(component.hasMore).toBeTrue();
  });

  it('creates a root reply and reports loading and submission failures', () => {
    const created = reply(2);
    replies.createReply.and.returnValues(
      of(replyResponse(created)),
      throwError(() => new Error('post failed')),
    );
    const counts: number[] = [];
    component.replyCountChange.subscribe((count) => counts.push(count));
    component.initialReplyCount = 4;
    component.ngOnInit();

    component.newReplyText = '   ';
    component.submitRoot();
    expect(replies.createReply).not.toHaveBeenCalled();

    component.newReplyText = '  New root  ';
    component.submitRoot();
    expect(replies.createReply).toHaveBeenCalledWith(4, 9, 'New root', null);
    expect(component.roots.map((item) => item.id)).toEqual([2]);
    expect(counts).toEqual([5]);
    expect(component.newReplyText).toBe('');

    component.newReplyText = 'Try again';
    component.submitRoot();
    expect(component.error).toBe('post failed');
    expect(component.submitting).toBeFalse();

    replies.getReplies.and.returnValue(throwError(() => new Error('load failed')));
    component.loadRoots(true);
    expect(component.error).toBe('load failed');
    expect(component.loadingMore).toBeFalse();
  });

  it('creates, edits, and soft-deletes replies through node actions', () => {
    const root = reply(1, null, 0);
    const child = reply(2, 1);
    replies.getReplies.and.returnValue(of(page([root])));
    replies.createReply.and.returnValue(of(replyResponse(child)));
    replies.updateReply.and.returnValue(
      of(replyResponse({ ...root, content: 'Edited', updatedAt: '2026-08-15T00:00:00Z' })),
    );
    replies.deleteReply.and.returnValue(
      of(replyResponse({ ...root, content: null, author: null, isDeleted: true })),
    );
    component.ngOnInit();
    const node = component.roots[0];
    node.childrenLoaded = true;
    node.replyOpen = true;
    node.replyText = 'draft';
    node.editOpen = true;
    node.deleteConfirm = true;

    component.handleNodeAction({ type: 'create', node, content: 'Child' });
    expect(replies.createReply).toHaveBeenCalledWith(4, 9, 'Child', 1);
    expect(node.children.map((item) => item.id)).toEqual([2]);
    expect(node.directReplyCount).toBe(1);
    expect(node.replyOpen).toBeFalse();

    component.handleNodeAction({ type: 'edit', node, content: 'Edited' });
    expect(node.content).toBe('Edited');
    expect(node.editOpen).toBeFalse();

    component.handleNodeAction({ type: 'delete', node });
    expect(node.isDeleted).toBeTrue();
    expect(node.content).toBeNull();
    expect(node.children.map((item) => item.id)).toEqual([2]);
    expect(node.deleteConfirm).toBeFalse();
  });

  it('surfaces child mutation failures and clears busy state', () => {
    replies.getReplies.and.returnValue(of(page([reply(1)])));
    replies.createReply.and.returnValue(throwError(() => new Error('create failed')));
    replies.updateReply.and.returnValue(throwError(() => new Error('edit failed')));
    replies.deleteReply.and.returnValue(throwError(() => new Error('delete failed')));
    component.ngOnInit();
    const node = component.roots[0];

    component.handleNodeAction({ type: 'create', node, content: 'Child' });
    expect(node.error).toBe('create failed');
    expect(node.busy).toBeFalse();

    component.handleNodeAction({ type: 'edit', node, content: 'Edit' });
    expect(node.error).toBe('edit failed');

    node.deleteConfirm = true;
    component.handleNodeAction({ type: 'delete', node });
    expect(node.error).toBe('delete failed');
    expect(node.deleteConfirm).toBeFalse();
    expect(node.busy).toBeFalse();
  });

  it('loads appended children, deduplicates them, and reports child load failures', () => {
    const root = reply(1, null, 2);
    replies.getReplies.and.returnValues(
      of(page([root])),
      of(page([reply(2, 1)], true, 'child-next')),
      of(page([reply(2, 1), reply(3, 1)])),
      throwError(() => new Error('children failed')),
    );
    component.ngOnInit();
    const node = component.roots[0];

    component.handleNodeAction({ type: 'loadChildren', node, append: false });
    component.handleNodeAction({ type: 'loadChildren', node, append: true });
    expect(replies.getReplies.calls.mostRecent().args).toEqual([4, 9, 1, 'Newest', 'child-next']);
    expect(node.children.map((item) => item.id)).toEqual([3, 2]);

    component.handleNodeAction({ type: 'loadChildren', node, append: false });
    expect(node.error).toBe('children failed');
    expect(node.loadingChildren).toBeFalse();
  });

  it('prompts anonymous users and supports setting, switching, and clearing reactions', () => {
    const root = reply(1);
    replies.getReplies.and.returnValue(of(page([root])));
    replies.setReaction.and.returnValues(
      of(reactionResponse(1, 1, 0, 'Like')),
      of(reactionResponse(1, 0, 1, 'Dislike')),
    );
    replies.clearReaction.and.returnValue(of(reactionResponse(1, 0, 0, null)));
    component.ngOnInit();
    const node = component.roots[0];

    component.handleNodeAction({ type: 'react', node, reaction: 'Like' });
    expect(node.error).toBe('Sign in to react to replies.');
    expect(replies.setReaction).not.toHaveBeenCalled();

    component.currentUser = { Id: 7 } as never;
    component.handleNodeAction({ type: 'react', node, reaction: 'Like' });
    expect(node.currentUserReaction).toBe('Like');
    expect(node.likeCount).toBe(1);

    component.handleNodeAction({ type: 'react', node, reaction: 'Dislike' });
    expect(node.currentUserReaction).toBe('Dislike');
    expect(node.likeCount).toBe(0);
    expect(node.dislikeCount).toBe(1);

    component.handleNodeAction({ type: 'react', node, reaction: 'Dislike' });
    expect(replies.clearReaction).toHaveBeenCalledWith(4, 9, 1);
    expect(node.currentUserReaction).toBeNull();
    expect(node.dislikeCount).toBe(0);
  });

  it('applies only relevant live updates and preserves the viewer reaction on edits', () => {
    const root = { ...reply(1), currentUserReaction: 'Like' as const, likeCount: 1 };
    replies.getReplies.and.returnValue(of(page([root])));
    component.ngOnInit();
    const node = component.roots[0];

    live.next({
      type: 'ReplyReactionChanged',
      discussionId: 99,
      replyId: 1,
      likeCount: 8,
      dislikeCount: 2,
    });
    expect(node.likeCount).toBe(1);

    live.next({
      type: 'ReplyReactionChanged',
      discussionId: 9,
      replyId: 1,
      likeCount: 3,
      dislikeCount: 2,
    });
    expect(node.likeCount).toBe(3);
    expect(node.dislikeCount).toBe(2);

    live.next({
      type: 'ReplyUpdated',
      reply: { ...root, content: 'Live edit', currentUserReaction: null },
    });
    expect(node.content).toBe('Live edit');
    expect(node.currentUserReaction).toBe('Like');

    live.next({ type: 'ReplyDeleted', reply: { ...root, content: null, isDeleted: true } });
    expect(node.isDeleted).toBeTrue();

    live.next({ type: 'ReplyCreated', reply: { ...reply(8), discussionId: 99 } });
    live.next({ type: 'ReplyCreated', reply: reply(9, 404) });
    expect(component.roots.map((item) => item.id)).toEqual([1]);
    expect(component.totalReplies).toBe(1);
  });
});

function reply(
  id: number,
  parentReplyId: number | null = null,
  directReplyCount = 0,
  createdAt = `2026-08-14T00:00:0${id}Z`,
): DiscussionReply {
  return {
    id,
    discussionId: 9,
    parentReplyId,
    userId: 7,
    content: `Reply ${id}`,
    author: { id: 7, name: 'Taylor', username: 'taylor', avatar: null },
    isDeleted: false,
    createdAt,
    updatedAt: createdAt,
    likeCount: 0,
    dislikeCount: 0,
    currentUserReaction: null,
    directReplyCount,
  };
}

function page(
  items: DiscussionReply[],
  hasMore = false,
  nextCursor: string | null = null,
): DiscussionReplyPageApiResponse {
  return {
    success: true,
    message: 'ok',
    data: { items, totalCount: items.length, nextCursor, hasMore },
    error: null,
    meta: null,
  };
}

function replyResponse(data: DiscussionReply): DiscussionReplyApiResponse {
  return { success: true, message: 'ok', data, error: null, meta: null };
}

function reactionResponse(
  replyId: number,
  likeCount: number,
  dislikeCount: number,
  currentUserReaction: 'Like' | 'Dislike' | null,
): DiscussionReplyReactionApiResponse {
  return {
    success: true,
    message: 'ok',
    data: { replyId, likeCount, dislikeCount, currentUserReaction },
    error: null,
    meta: null,
  };
}
