import { of } from 'rxjs';

import { DiscussionReply } from '../../models/club-discussion.types';
import { PostComment } from '../../models/club-post.types';
import { ClubRealtimeEvent } from '../../services/club-realtime.service';
import { createDiscussionThreadSource, createPostThreadSource } from './thread-data-source';

function reply(overrides: Partial<DiscussionReply> = {}): DiscussionReply {
  return {
    id: 1,
    discussionId: 9,
    parentReplyId: null,
    userId: 7,
    content: 'Hi',
    author: null,
    isDeleted: false,
    createdAt: '2026-08-15T12:00:00Z',
    updatedAt: '2026-08-15T12:00:00Z',
    likeCount: 0,
    dislikeCount: 0,
    currentUserReaction: null,
    directReplyCount: 0,
    ...overrides,
  };
}

function comment(overrides: Partial<PostComment> = {}): PostComment {
  return {
    id: 1,
    postId: 4,
    parentCommentId: null,
    userId: 7,
    content: 'Hi',
    author: null,
    isDeleted: false,
    createdAt: '2026-08-15T12:00:00Z',
    updatedAt: '2026-08-15T12:00:00Z',
    likeCount: 0,
    dislikeCount: 0,
    currentUserReaction: null,
    directReplyCount: 0,
    ...overrides,
  };
}

describe('createDiscussionThreadSource', () => {
  let service: jasmine.SpyObj<{
    getReplies: unknown;
    createReply: unknown;
    updateReply: unknown;
    deleteReply: unknown;
    setReaction: unknown;
    clearReaction: unknown;
  }>;

  beforeEach(() => {
    service = jasmine.createSpyObj('DiscussionRepliesService', [
      'getReplies',
      'createReply',
      'updateReply',
      'deleteReply',
      'setReaction',
      'clearReaction',
    ]);
  });

  function build() {
    return createDiscussionThreadSource(service as never, 1, 9);
  }

  it('describes itself as a discussion thread', () => {
    const source = build();

    expect(source.kind).toBe('discussion');
    expect(source.clubId).toBe(1);
    expect(source.threadId).toBe(9);
    expect(source.labels.heading).toBe('Replies');
  });

  it('forwards calls to the replies service and unwraps the envelope', () => {
    const page = { items: [reply()], totalCount: 1, nextCursor: null, hasMore: false };
    (service.getReplies as jasmine.Spy).and.returnValue(of({ data: page }));
    (service.createReply as jasmine.Spy).and.returnValue(of({ data: reply({ id: 3 }) }));

    const source = build();
    let listed: unknown = null;
    source.list(null, 'Newest', 'cursor').subscribe((value) => (listed = value));
    expect(service.getReplies).toHaveBeenCalledWith(1, 9, null, 'Newest', 'cursor');
    expect(listed).toBe(page);

    let created: DiscussionReply | null = null;
    source.create('Body', 2).subscribe((value) => (created = value));
    expect(service.createReply).toHaveBeenCalledWith(1, 9, 'Body', 2);
    expect(created!.id).toBe(3);
  });

  it('maps a missing envelope body to null', () => {
    (service.deleteReply as jasmine.Spy).and.returnValue(of({ data: undefined }));

    let removed: DiscussionReply | null = 'unset' as never;
    build()
      .remove(5)
      .subscribe((value) => (removed = value));

    expect(removed).toBeNull();
  });

  it('claims reply events for its own discussion only', () => {
    const source = build();
    const mine = reply({ id: 5, discussionId: 9 });
    const theirs = reply({ id: 6, discussionId: 10 });

    expect(source.matchLiveEvent({ type: 'ReplyCreated', reply: mine })).toEqual({
      kind: 'created',
      item: mine,
    });
    expect(source.matchLiveEvent({ type: 'ReplyUpdated', reply: mine })).toEqual({
      kind: 'updated',
      item: mine,
    });
    expect(source.matchLiveEvent({ type: 'ReplyDeleted', reply: mine })).toEqual({
      kind: 'deleted',
      item: mine,
    });
    expect(source.matchLiveEvent({ type: 'ReplyCreated', reply: theirs })).toBeNull();
  });

  it('claims reaction events for its own discussion only', () => {
    const source = build();

    expect(
      source.matchLiveEvent({
        type: 'ReplyReactionChanged',
        discussionId: 9,
        replyId: 5,
        likeCount: 2,
        dislikeCount: 1,
      }),
    ).toEqual({ kind: 'reaction', itemId: 5, likeCount: 2, dislikeCount: 1 });

    expect(
      source.matchLiveEvent({
        type: 'ReplyReactionChanged',
        discussionId: 10,
        replyId: 5,
        likeCount: 2,
        dislikeCount: 1,
      }),
    ).toBeNull();
  });

  it('ignores comment events entirely', () => {
    const source = build();

    expect(source.matchLiveEvent({ type: 'CommentCreated', comment: comment() })).toBeNull();
    expect(source.matchLiveEvent({ type: 'Connected' } as ClubRealtimeEvent)).toBeNull();
  });

  it('reads the parent id from the reply shape', () => {
    expect(build().parentIdOf(reply({ parentReplyId: 4 }))).toBe(4);
    expect(build().parentIdOf(reply())).toBeNull();
  });
});

describe('createPostThreadSource', () => {
  let service: jasmine.SpyObj<{
    getComments: unknown;
    createComment: unknown;
    updateComment: unknown;
    deleteComment: unknown;
    setReaction: unknown;
    clearReaction: unknown;
  }>;

  beforeEach(() => {
    service = jasmine.createSpyObj('PostCommentsService', [
      'getComments',
      'createComment',
      'updateComment',
      'deleteComment',
      'setReaction',
      'clearReaction',
    ]);
  });

  function build() {
    return createPostThreadSource(service as never, 1, 4);
  }

  it('describes itself as a post thread', () => {
    const source = build();

    expect(source.kind).toBe('post');
    expect(source.threadId).toBe(4);
    expect(source.labels.heading).toBe('Comments');
  });

  it('forwards mutations to the comments service', () => {
    (service.updateComment as jasmine.Spy).and.returnValue(of({ data: comment({ id: 8 }) }));
    (service.setReaction as jasmine.Spy).and.returnValue(
      of({ data: { likeCount: 1, dislikeCount: 0, currentUserReaction: 'Like' } }),
    );

    const source = build();
    source.update(8, 'Edited').subscribe();
    expect(service.updateComment).toHaveBeenCalledWith(1, 4, 8, 'Edited');

    source.react(8, 'Like').subscribe();
    expect(service.setReaction).toHaveBeenCalledWith(1, 4, 8, 'Like');
  });

  it('claims comment events for its own post only', () => {
    const source = build();
    const mine = comment({ id: 8, postId: 4 });
    const theirs = comment({ id: 9, postId: 5 });

    expect(source.matchLiveEvent({ type: 'CommentCreated', comment: mine })).toEqual({
      kind: 'created',
      item: mine,
    });
    expect(source.matchLiveEvent({ type: 'CommentDeleted', comment: theirs })).toBeNull();

    expect(
      source.matchLiveEvent({
        type: 'CommentReactionChanged',
        postId: 4,
        commentId: 8,
        likeCount: 3,
        dislikeCount: 0,
      }),
    ).toEqual({ kind: 'reaction', itemId: 8, likeCount: 3, dislikeCount: 0 });

    expect(
      source.matchLiveEvent({
        type: 'CommentReactionChanged',
        postId: 5,
        commentId: 8,
        likeCount: 3,
        dislikeCount: 0,
      }),
    ).toBeNull();
  });

  it('ignores discussion reply events entirely', () => {
    expect(build().matchLiveEvent({ type: 'ReplyCreated', reply: reply() })).toBeNull();
  });

  it('reads the parent id from the comment shape', () => {
    expect(build().parentIdOf(comment({ parentCommentId: 2 }))).toBe(2);
    expect(build().parentIdOf(comment())).toBeNull();
  });
});

describe('thread data source envelope handling', () => {
  const methods = [
    'getReplies',
    'createReply',
    'updateReply',
    'deleteReply',
    'setReaction',
    'clearReaction',
  ];

  /** Every adapter method maps a missing envelope body to null and passes data through. */
  function exercise(source: {
    list: (...args: never[]) => { subscribe: (fn: (v: unknown) => void) => void };
    create: (...args: never[]) => { subscribe: (fn: (v: unknown) => void) => void };
    update: (...args: never[]) => { subscribe: (fn: (v: unknown) => void) => void };
    remove: (...args: never[]) => { subscribe: (fn: (v: unknown) => void) => void };
    react: (...args: never[]) => { subscribe: (fn: (v: unknown) => void) => void };
    clearReaction: (...args: never[]) => { subscribe: (fn: (v: unknown) => void) => void };
  }): unknown[] {
    const seen: unknown[] = [];
    const push = (value: unknown) => seen.push(value);
    source.list(null as never, 'Newest' as never, null as never).subscribe(push);
    source.create('x' as never, null as never).subscribe(push);
    source.update(1 as never, 'x' as never).subscribe(push);
    source.remove(1 as never).subscribe(push);
    source.react(1 as never, 'Like' as never).subscribe(push);
    source.clearReaction(1 as never).subscribe(push);
    return seen;
  }

  it('returns null from every discussion method when the envelope has no body', () => {
    const service = jasmine.createSpyObj('DiscussionRepliesService', methods);
    methods.forEach((name) =>
      (service[name] as jasmine.Spy).and.returnValue(of({ data: undefined })),
    );

    const seen = exercise(createDiscussionThreadSource(service as never, 1, 9) as never);

    expect(seen.length).toBe(6);
    expect(seen.every((value) => value === null)).toBeTrue();
  });

  it('returns null from every post method when the envelope has no body', () => {
    const postMethods = [
      'getComments',
      'createComment',
      'updateComment',
      'deleteComment',
      'setReaction',
      'clearReaction',
    ];
    const service = jasmine.createSpyObj('PostCommentsService', postMethods);
    postMethods.forEach((name) =>
      (service[name] as jasmine.Spy).and.returnValue(of({ data: null })),
    );

    const seen = exercise(createPostThreadSource(service as never, 1, 4) as never);

    expect(seen.length).toBe(6);
    expect(seen.every((value) => value === null)).toBeTrue();
  });

  it('passes a present body straight through for every discussion method', () => {
    const service = jasmine.createSpyObj('DiscussionRepliesService', methods);
    methods.forEach((name) =>
      (service[name] as jasmine.Spy).and.returnValue(of({ data: { marker: name } })),
    );

    const seen = exercise(createDiscussionThreadSource(service as never, 1, 9) as never);

    expect(seen.every((value) => value !== null)).toBeTrue();
  });

  it('passes a present body straight through for every post method', () => {
    const postMethods = [
      'getComments',
      'createComment',
      'updateComment',
      'deleteComment',
      'setReaction',
      'clearReaction',
    ];
    const service = jasmine.createSpyObj('PostCommentsService', postMethods);
    postMethods.forEach((name) =>
      (service[name] as jasmine.Spy).and.returnValue(of({ data: { marker: name } })),
    );

    const seen = exercise(createPostThreadSource(service as never, 1, 4) as never);

    expect(seen.every((value) => value !== null)).toBeTrue();
  });
});
