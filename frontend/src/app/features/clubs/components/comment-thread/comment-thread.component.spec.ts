import { of, Subject, throwError } from 'rxjs';

import {
  PostComment,
  PostCommentApiResponse,
  PostCommentsApiResponse,
  PostCommentReactionApiResponse,
} from '../../models/club-post.types';
import { PostCommentsService } from '../../services/post-comments.service';
import { SseCommentEvent, CommentSseService } from '../../services/comment-sse.service';
import { CommentThreadComponent } from './comment-thread.component';

describe('CommentThreadComponent', () => {
  let comments: jasmine.SpyObj<PostCommentsService>;
  let live: Subject<SseCommentEvent>;
  let component: CommentThreadComponent;

  beforeEach(() => {
    comments = jasmine.createSpyObj<PostCommentsService>('PostCommentsService', [
      'getComments',
      'createComment',
      'updateComment',
      'deleteComment',
      'setReaction',
      'clearReaction',
    ]);
    live = new Subject<SseCommentEvent>();
    const sse = {
      connect: () => live.asObservable(),
    } as unknown as CommentSseService;
    comments.getComments.and.returnValue(of(page([])));
    component = new CommentThreadComponent(comments, sse);
    component.clubId = 4;
    component.postId = 9;
  });

  afterEach(() => component.ngOnDestroy());

  it('loads children lazily at the selected sort level', () => {
    const root = comment(1, null, 1);
    comments.getComments.and.returnValues(of(page([root])), of(page([comment(2, 1)])));
    component.ngOnInit();

    component.handleNodeAction({ type: 'loadChildren', node: component.roots[0], append: false });

    expect(comments.getComments.calls.mostRecent().args).toEqual([4, 9, 1, 'Newest', null]);
    expect(component.roots[0].children.map((item) => item.id)).toEqual([2]);
  });

  it('keeps live comments correctly ordered and deduplicated across Oldest pages', () => {
    component.sort = 'Oldest';
    comments.getComments.and.returnValues(
      of(page([comment(1, null, 0, '2026-08-14T01:00:00Z')], true, 'next')),
      of(
        page([
          comment(2, null, 0, '2026-08-14T02:00:00Z'),
          comment(3, null, 0, '2026-08-14T03:00:00Z'),
        ]),
      ),
    );
    component.initialCommentCount = 2;
    component.ngOnInit();
    live.next({ type: 'CommentCreated', comment: comment(3, null, 0, '2026-08-14T03:00:00Z') });

    component.loadRoots(true);

    expect(component.roots.map((item) => item.id)).toEqual([1, 2, 3]);
    expect(component.totalComments).toBe(3);
  });

  it('reloads roots and expanded branches when the stream reconnects', () => {
    const root = comment(1, null, 1);
    comments.getComments.and.returnValue(of(page([root])));
    component.ngOnInit();
    component.roots[0].childrenLoaded = true;

    live.next({ type: 'Connected' });

    expect(comments.getComments.calls.allArgs()).toContain([4, 9, null, 'Newest', null]);
    expect(comments.getComments.calls.allArgs()).toContain([4, 9, 1, 'Newest', null]);
  });

  it('reconciles an initially empty thread after a connection arrives during loading', () => {
    const initial = new Subject<PostCommentsApiResponse>();
    comments.getComments.and.returnValues(initial.asObservable(), of(page([comment(1)])));
    const counts: number[] = [];
    component.commentCountChange.subscribe((count) => counts.push(count));
    component.ngOnInit();

    live.next({ type: 'Connected' });
    expect(comments.getComments).toHaveBeenCalledTimes(1);

    initial.next(page([]));
    initial.complete();

    expect(comments.getComments).toHaveBeenCalledTimes(2);
    expect(component.roots.map((item) => item.id)).toEqual([1]);
    expect(component.totalComments).toBe(1);
    expect(counts).toEqual([1]);
  });

  it('reconciles after the initial empty request has already completed', () => {
    comments.getComments.and.returnValues(of(page([])), of(page([comment(1)])));
    component.ngOnInit();

    live.next({ type: 'Connected' });

    expect(comments.getComments).toHaveBeenCalledTimes(2);
    expect(component.roots.map((item) => item.id)).toEqual([1]);
    expect(component.totalComments).toBe(1);
  });

  it('preserves paginated roots and children during reconnect reconciliation', () => {
    const root = comment(1, null, 2);
    comments.getComments.and.returnValues(
      of(page([root], true, 'root-next', 3)),
      of(page([comment(4)], true, 'root-later', 3)),
      of(page([comment(2, 1)], true, 'child-next', 2)),
      of(page([comment(3, 1)], false, null, 2)),
      of(page([root], true, 'first-root-cursor', 3)),
      of(page([comment(2, 1)], true, 'first-child-cursor', 2)),
    );
    component.initialCommentCount = 5;
    component.ngOnInit();
    component.loadRoots(true);
    const node = component.roots.find((item) => item.id === 1)!;
    component.handleNodeAction({ type: 'loadChildren', node, append: false });
    component.handleNodeAction({ type: 'loadChildren', node, append: true });

    live.next({ type: 'Connected' });

    expect(component.roots.map((item) => item.id)).toEqual([4, 1]);
    expect(node.children.map((item) => item.id)).toEqual([3, 2]);
    expect(node.nextCursor).toBeNull();
    expect(node.hasMoreChildren).toBeFalse();
    expect(component.nextCursor).toBe('root-later');
    expect(component.hasMore).toBeTrue();
    expect(component.totalComments).toBe(5);
  });

  it('rolls back an optimistic reaction when the API request fails', () => {
    const root = comment(1);
    comments.getComments.and.returnValue(of(page([root])));
    comments.setReaction.and.returnValue(throwError(() => new Error('offline')));
    component.currentUser = { Id: 7 } as never;
    component.ngOnInit();

    component.handleNodeAction({ type: 'react', node: component.roots[0], reaction: 'Like' });

    expect(component.roots[0].likeCount).toBe(0);
    expect(component.roots[0].currentUserReaction).toBeNull();
    expect(component.roots[0].error).toBe('offline');
  });

  it('resets cached pages when the sort changes and ignores the selected sort', () => {
    comments.getComments.and.returnValue(of(page([comment(1)], true, 'next')));
    component.ngOnInit();

    component.changeSort('Newest');
    expect(comments.getComments).toHaveBeenCalledTimes(1);

    component.changeSort('Oldest');

    expect(comments.getComments.calls.mostRecent().args).toEqual([4, 9, null, 'Oldest', null]);
    expect(component.nextCursor).toBe('next');
    expect(component.hasMore).toBeTrue();
  });

  it('creates a root comment and reports loading and submission failures', () => {
    const created = comment(2);
    comments.createComment.and.returnValues(
      of(commentResponse(created)),
      throwError(() => new Error('post failed')),
    );
    const counts: number[] = [];
    component.commentCountChange.subscribe((count) => counts.push(count));
    component.initialCommentCount = 4;
    component.ngOnInit();

    component.newCommentText = '   ';
    component.submitRoot();
    expect(comments.createComment).not.toHaveBeenCalled();

    component.newCommentText = '  New root  ';
    component.submitRoot();
    expect(comments.createComment).toHaveBeenCalledWith(4, 9, 'New root', null);
    expect(component.roots.map((item) => item.id)).toEqual([2]);
    expect(counts).toEqual([5]);
    expect(component.newCommentText).toBe('');

    component.newCommentText = 'Try again';
    component.submitRoot();
    expect(component.error).toBe('post failed');
    expect(component.submitting).toBeFalse();

    comments.getComments.and.returnValue(throwError(() => new Error('load failed')));
    component.loadRoots(true);
    expect(component.error).toBe('load failed');
    expect(component.loadingMore).toBeFalse();
  });

  it('creates, edits, and soft-deletes comments through node actions', () => {
    const root = comment(1, null, 0);
    const child = comment(2, 1);
    comments.getComments.and.returnValue(of(page([root])));
    comments.createComment.and.returnValue(of(commentResponse(child)));
    comments.updateComment.and.returnValue(
      of(commentResponse({ ...root, content: 'Edited', updatedAt: '2026-08-15T00:00:00Z' })),
    );
    comments.deleteComment.and.returnValue(
      of(commentResponse({ ...root, content: null, author: null, isDeleted: true })),
    );
    component.ngOnInit();
    const node = component.roots[0];
    node.childrenLoaded = true;
    node.replyOpen = true;
    node.replyText = 'draft';
    node.editOpen = true;
    node.deleteConfirm = true;

    component.handleNodeAction({ type: 'create', node, content: 'Child' });
    expect(comments.createComment).toHaveBeenCalledWith(4, 9, 'Child', 1);
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
    comments.getComments.and.returnValue(of(page([comment(1)])));
    comments.createComment.and.returnValue(throwError(() => new Error('create failed')));
    comments.updateComment.and.returnValue(throwError(() => new Error('edit failed')));
    comments.deleteComment.and.returnValue(throwError(() => new Error('delete failed')));
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
    const root = comment(1, null, 2);
    comments.getComments.and.returnValues(
      of(page([root])),
      of(page([comment(2, 1)], true, 'child-next')),
      of(page([comment(2, 1), comment(3, 1)])),
      throwError(() => new Error('children failed')),
    );
    component.ngOnInit();
    const node = component.roots[0];

    component.handleNodeAction({ type: 'loadChildren', node, append: false });
    component.handleNodeAction({ type: 'loadChildren', node, append: true });
    expect(comments.getComments.calls.mostRecent().args).toEqual([4, 9, 1, 'Newest', 'child-next']);
    expect(node.children.map((item) => item.id)).toEqual([3, 2]);

    component.handleNodeAction({ type: 'loadChildren', node, append: false });
    expect(node.error).toBe('children failed');
    expect(node.loadingChildren).toBeFalse();
  });

  it('prompts anonymous users and supports setting, switching, and clearing reactions', () => {
    const root = comment(1);
    comments.getComments.and.returnValue(of(page([root])));
    comments.setReaction.and.returnValues(
      of(reactionResponse(1, 1, 0, 'Like')),
      of(reactionResponse(1, 0, 1, 'Dislike')),
    );
    comments.clearReaction.and.returnValue(of(reactionResponse(1, 0, 0, null)));
    component.ngOnInit();
    const node = component.roots[0];

    component.handleNodeAction({ type: 'react', node, reaction: 'Like' });
    expect(node.error).toBe('Sign in to react to comments.');
    expect(comments.setReaction).not.toHaveBeenCalled();

    component.currentUser = { Id: 7 } as never;
    component.handleNodeAction({ type: 'react', node, reaction: 'Like' });
    expect(node.currentUserReaction).toBe('Like');
    expect(node.likeCount).toBe(1);

    component.handleNodeAction({ type: 'react', node, reaction: 'Dislike' });
    expect(node.currentUserReaction).toBe('Dislike');
    expect(node.likeCount).toBe(0);
    expect(node.dislikeCount).toBe(1);

    component.handleNodeAction({ type: 'react', node, reaction: 'Dislike' });
    expect(comments.clearReaction).toHaveBeenCalledWith(4, 9, 1);
    expect(node.currentUserReaction).toBeNull();
    expect(node.dislikeCount).toBe(0);
  });

  it('applies only relevant live updates and preserves the viewer reaction on edits', () => {
    const root = { ...comment(1), currentUserReaction: 'Like' as const, likeCount: 1 };
    comments.getComments.and.returnValue(of(page([root])));
    component.ngOnInit();
    const node = component.roots[0];

    live.next({
      type: 'CommentReactionChanged',
      postId: 99,
      commentId: 1,
      likeCount: 8,
      dislikeCount: 2,
    });
    expect(node.likeCount).toBe(1);

    live.next({
      type: 'CommentReactionChanged',
      postId: 9,
      commentId: 1,
      likeCount: 3,
      dislikeCount: 2,
    });
    expect(node.likeCount).toBe(3);
    expect(node.dislikeCount).toBe(2);

    live.next({
      type: 'CommentUpdated',
      comment: { ...root, content: 'Live edit', currentUserReaction: null },
    });
    expect(node.content).toBe('Live edit');
    expect(node.currentUserReaction).toBe('Like');

    live.next({ type: 'CommentDeleted', comment: { ...root, content: null, isDeleted: true } });
    expect(node.isDeleted).toBeTrue();

    live.next({ type: 'CommentCreated', comment: { ...comment(8), postId: 99 } });
    live.next({ type: 'CommentCreated', comment: comment(9, 404) });
    expect(component.roots.map((item) => item.id)).toEqual([1]);
    expect(component.totalComments).toBe(1);
  });
});

function comment(
  id: number,
  parentCommentId: number | null = null,
  directReplyCount = 0,
  createdAt = `2026-08-14T00:00:0${id}Z`,
): PostComment {
  return {
    id,
    postId: 9,
    parentCommentId,
    userId: 7,
    content: `Comment ${id}`,
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
  items: PostComment[],
  hasMore = false,
  nextCursor: string | null = null,
  totalCount = items.length,
): PostCommentsApiResponse {
  return {
    success: true,
    message: 'ok',
    data: { items, totalCount, nextCursor, hasMore },
    error: null,
    meta: null,
  };
}

function commentResponse(data: PostComment): PostCommentApiResponse {
  return { success: true, message: 'ok', data, error: null, meta: null };
}

function reactionResponse(
  commentId: number,
  likeCount: number,
  dislikeCount: number,
  currentUserReaction: 'Like' | 'Dislike' | null,
): PostCommentReactionApiResponse {
  return {
    success: true,
    message: 'ok',
    data: { commentId, likeCount, dislikeCount, currentUserReaction },
    error: null,
    meta: null,
  };
}
