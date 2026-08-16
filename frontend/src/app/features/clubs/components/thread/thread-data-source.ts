import { Observable, map } from 'rxjs';

import { DiscussionReply } from '../../models/club-discussion.types';
import { PostComment } from '../../models/club-post.types';
import { DiscussionRepliesService } from '../../services/discussion-replies.service';
import { PostCommentsService } from '../../services/post-comments.service';
import { ClubRealtimeEvent, ThreadKind } from '../../services/club-realtime.service';
import { ThreadItem, ThreadSort } from '../thread-tree/thread-tree-state';

export type ThreadReaction = 'Like' | 'Dislike';

export interface ThreadPage<TItem> {
  items: TItem[];
  totalCount: number;
  nextCursor: string | null;
  hasMore: boolean;
}

export interface ThreadReactionResult {
  likeCount: number;
  dislikeCount: number;
  currentUserReaction: ThreadReaction | null;
}

/** A live change that concerns this thread, already stripped of discussion/post specifics. */
export type ThreadLiveChange<TItem> =
  | { kind: 'created'; item: TItem }
  | { kind: 'updated'; item: TItem }
  | { kind: 'deleted'; item: TItem }
  | { kind: 'reaction'; itemId: number; likeCount: number; dislikeCount: number };

/** The words that differ between a discussion thread and a post comment thread. */
export interface ThreadLabels {
  heading: string;
  singular: string;
  plural: string;
  composerPlaceholder: string;
  replyPlaceholder: string;
  emptyTitle: string;
  emptyBody: string;
  deletedText: string;
  deleteConfirm: string;
  signInPrompt: string;
}

/**
 * Everything the generic thread component needs in order to drive either club discussion
 * replies or club post comments.
 *
 * The two stacks were byte-for-byte identical apart from names, so the differences live
 * here instead of in a second copy of the component.
 */
export interface ThreadDataSource<TItem extends ThreadItem> {
  readonly kind: ThreadKind;
  readonly clubId: number;
  readonly threadId: number;
  readonly labels: ThreadLabels;

  list(
    parentId: number | null,
    sort: ThreadSort,
    cursor: string | null,
  ): Observable<ThreadPage<TItem> | null>;
  create(content: string, parentId: number | null): Observable<TItem | null>;
  update(itemId: number, content: string): Observable<TItem | null>;
  remove(itemId: number): Observable<TItem | null>;
  react(itemId: number, reaction: ThreadReaction): Observable<ThreadReactionResult | null>;
  clearReaction(itemId: number): Observable<ThreadReactionResult | null>;

  parentIdOf(item: TItem): number | null;
  /** Live events are club-wide, so each one has to be matched back to this thread. */
  matchLiveEvent(event: ClubRealtimeEvent): ThreadLiveChange<TItem> | null;
}

const DISCUSSION_LABELS: ThreadLabels = {
  heading: 'Replies',
  singular: 'reply',
  plural: 'replies',
  composerPlaceholder: 'Join the discussion...',
  replyPlaceholder: 'Write a reply...',
  emptyTitle: 'No replies yet',
  emptyBody: 'Start the conversation.',
  deletedText: 'Reply deleted',
  deleteConfirm: 'Delete this reply?',
  signInPrompt: 'to reply or react.',
};

const POST_LABELS: ThreadLabels = {
  heading: 'Comments',
  singular: 'comment',
  plural: 'comments',
  composerPlaceholder: 'Add a comment...',
  replyPlaceholder: 'Write a reply...',
  emptyTitle: 'No comments yet',
  emptyBody: 'Be the first to comment.',
  deletedText: 'Comment deleted',
  deleteConfirm: 'Delete this comment?',
  signInPrompt: 'to comment or react.',
};

export function createDiscussionThreadSource(
  service: DiscussionRepliesService,
  clubId: number,
  discussionId: number,
): ThreadDataSource<DiscussionReply> {
  return {
    kind: 'discussion',
    clubId,
    threadId: discussionId,
    labels: DISCUSSION_LABELS,

    list: (parentId, sort, cursor) =>
      service
        .getReplies(clubId, discussionId, parentId, sort, cursor)
        .pipe(map((response) => response.data ?? null)),
    create: (content, parentId) =>
      service
        .createReply(clubId, discussionId, content, parentId)
        .pipe(map((response) => response.data ?? null)),
    update: (itemId, content) =>
      service
        .updateReply(clubId, discussionId, itemId, content)
        .pipe(map((response) => response.data ?? null)),
    remove: (itemId) =>
      service
        .deleteReply(clubId, discussionId, itemId)
        .pipe(map((response) => response.data ?? null)),
    react: (itemId, reaction) =>
      service
        .setReaction(clubId, discussionId, itemId, reaction)
        .pipe(map((response) => response.data ?? null)),
    clearReaction: (itemId) =>
      service
        .clearReaction(clubId, discussionId, itemId)
        .pipe(map((response) => response.data ?? null)),

    parentIdOf: (item) => item.parentReplyId,

    matchLiveEvent: (event) => {
      if (event.type === 'ReplyReactionChanged') {
        return event.discussionId === discussionId
          ? {
              kind: 'reaction',
              itemId: event.replyId,
              likeCount: event.likeCount,
              dislikeCount: event.dislikeCount,
            }
          : null;
      }
      if (
        event.type !== 'ReplyCreated' &&
        event.type !== 'ReplyUpdated' &&
        event.type !== 'ReplyDeleted'
      ) {
        return null;
      }
      // The club group carries every discussion in the club, so filter to this one.
      if (event.reply.discussionId !== discussionId) return null;
      if (event.type === 'ReplyCreated') return { kind: 'created', item: event.reply };
      if (event.type === 'ReplyUpdated') return { kind: 'updated', item: event.reply };
      return { kind: 'deleted', item: event.reply };
    },
  };
}

export function createPostThreadSource(
  service: PostCommentsService,
  clubId: number,
  postId: number,
): ThreadDataSource<PostComment> {
  return {
    kind: 'post',
    clubId,
    threadId: postId,
    labels: POST_LABELS,

    list: (parentId, sort, cursor) =>
      service
        .getComments(clubId, postId, parentId, sort, cursor)
        .pipe(map((response) => response.data ?? null)),
    create: (content, parentId) =>
      service
        .createComment(clubId, postId, content, parentId)
        .pipe(map((response) => response.data ?? null)),
    update: (itemId, content) =>
      service
        .updateComment(clubId, postId, itemId, content)
        .pipe(map((response) => response.data ?? null)),
    remove: (itemId) =>
      service.deleteComment(clubId, postId, itemId).pipe(map((response) => response.data ?? null)),
    react: (itemId, reaction) =>
      service
        .setReaction(clubId, postId, itemId, reaction)
        .pipe(map((response) => response.data ?? null)),
    clearReaction: (itemId) =>
      service.clearReaction(clubId, postId, itemId).pipe(map((response) => response.data ?? null)),

    parentIdOf: (item) => item.parentCommentId,

    matchLiveEvent: (event) => {
      if (event.type === 'CommentReactionChanged') {
        return event.postId === postId
          ? {
              kind: 'reaction',
              itemId: event.commentId,
              likeCount: event.likeCount,
              dislikeCount: event.dislikeCount,
            }
          : null;
      }
      if (
        event.type !== 'CommentCreated' &&
        event.type !== 'CommentUpdated' &&
        event.type !== 'CommentDeleted'
      ) {
        return null;
      }
      if (event.comment.postId !== postId) return null;
      if (event.type === 'CommentCreated') return { kind: 'created', item: event.comment };
      if (event.type === 'CommentUpdated') return { kind: 'updated', item: event.comment };
      return { kind: 'deleted', item: event.comment };
    },
  };
}
