import { of, Subject, throwError } from 'rxjs';

import {
  DiscussionReply,
  DiscussionReplyPageApiResponse,
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
