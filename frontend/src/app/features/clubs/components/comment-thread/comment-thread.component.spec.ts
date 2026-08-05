import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { envelope, makeCurrentUser, provideTestStore } from '@testing';

import { CommentThreadComponent } from './comment-thread.component';
import { PostCommentsService } from '../../services/post-comments.service';
import { CommentSseService, SseCommentEvent } from '../../services/comment-sse.service';
import {
  PostComment,
  PostCommentsApiResponse,
  PostCommentsPagedData,
} from '../../models/club-post.types';

function makeComment(overrides: Partial<PostComment> = {}): PostComment {
  return {
    id: 1,
    postId: 9,
    userId: 1,
    content: 'Nice one',
    author: { id: 1, name: 'Test Member', username: 'member', avatar: null },
    createdAt: '2026-08-05T12:00:00Z',
    updatedAt: '2026-08-05T12:00:00Z',
    ...overrides,
  };
}

function page(
  items: PostComment[],
  overrides: Partial<PostCommentsPagedData> = {},
): PostCommentsPagedData {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 20,
    totalPages: 1,
    ...overrides,
  };
}

describe('CommentThreadComponent', () => {
  let fixture: ComponentFixture<CommentThreadComponent>;
  let component: CommentThreadComponent;
  let commentsService: jasmine.SpyObj<PostCommentsService>;
  let sse: Subject<SseCommentEvent>;

  beforeEach(async () => {
    commentsService = jasmine.createSpyObj<PostCommentsService>('PostCommentsService', [
      'getComments',
      'createComment',
      'updateComment',
      'deleteComment',
    ]);
    commentsService.getComments.and.returnValue(of(envelope(page([makeComment()]))));
    sse = new Subject<SseCommentEvent>();

    await TestBed.configureTestingModule({
      imports: [CommentThreadComponent],
      providers: [
        { provide: PostCommentsService, useValue: commentsService },
        { provide: CommentSseService, useValue: { connect: () => sse.asObservable() } },
        ...provideTestStore({ user: makeCurrentUser({ Id: 1 }) }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CommentThreadComponent);
    component = fixture.componentInstance;
    component.clubId = 3;
    component.postId = 9;
  });

  describe('initial load', () => {
    it('fetches the first page and records the signed-in user', () => {
      fixture.detectChanges();

      expect(commentsService.getComments).toHaveBeenCalledOnceWith(3, 9, 1, 20);
      expect(component.comments.length).toBe(1);
      expect(component.isLoggedIn).toBeTrue();
      expect(component.loading).toBeFalse();
    });

    it('derives totalPages when the API omits it', () => {
      commentsService.getComments.and.returnValue(
        of(envelope(page([makeComment()], { totalCount: 45, totalPages: 0 }))),
      );

      fixture.detectChanges();

      expect(component.totalPages).toBe(3);
    });

    it('surfaces a load failure', () => {
      commentsService.getComments.and.returnValue(
        throwError(() => ({ error: { message: 'Comments are unavailable.' } })),
      );

      fixture.detectChanges();

      expect(component.loadError).toBe('Comments are unavailable.');
      expect(component.loading).toBeFalse();
    });

    it('reads a PascalCase failure message', () => {
      commentsService.getComments.and.returnValue(
        throwError(() => ({ error: { Message: 'Comments are unavailable.' } })),
      );

      fixture.detectChanges();

      expect(component.loadError).toBe('Comments are unavailable.');
    });

    it('falls back to a generic load message', () => {
      commentsService.getComments.and.returnValue(throwError(() => ({})));

      fixture.detectChanges();

      expect(component.loadError).toBe('Failed to load comments.');
    });

    it('leaves the list untouched when the envelope carries no data', () => {
      commentsService.getComments.and.returnValue(of(envelope<PostCommentsPagedData>(null)));

      fixture.detectChanges();

      expect(component.comments).toEqual([]);
      expect(component.loading).toBeFalse();
    });

    it('ignores a created comment the API did not return', () => {
      fixture.detectChanges();
      commentsService.createComment.and.returnValue(of(envelope<PostComment>(null)));
      component.newCommentText = 'Fresh';

      component.submitComment();

      expect(component.comments.length).toBe(1);
      expect(component.newCommentText).toBe('');
    });

    it('ignores an edit the API did not return', () => {
      fixture.detectChanges();
      commentsService.updateComment.and.returnValue(of(envelope<PostComment>(null)));
      component.startEdit(component.comments[0]);
      component.editText = 'Edited';

      component.saveEdit(component.comments[0]);

      expect(component.comments[0].content).toBe('Nice one');
      expect(component.editingId).toBeNull();
    });
  });

  describe('generic failure messages', () => {
    beforeEach(() => fixture.detectChanges());

    it('falls back when posting fails with no body', () => {
      commentsService.createComment.and.returnValue(throwError(() => ({})));
      component.newCommentText = 'Fresh';

      component.submitComment();

      expect(component.submitError).toBe('Failed to post comment.');
    });

    it('falls back when an edit fails with no body', () => {
      commentsService.updateComment.and.returnValue(throwError(() => ({})));
      component.startEdit(component.comments[0]);
      component.editText = 'Edited';

      component.saveEdit(component.comments[0]);

      expect(component.submitError).toBe('Failed to update comment.');
    });

    it('falls back when a delete fails with no body', () => {
      commentsService.deleteComment.and.returnValue(throwError(() => ({})));

      component.deleteComment(1);

      expect(component.submitError).toBe('Failed to delete comment.');
    });
  });

  describe('loadMore', () => {
    beforeEach(() => {
      commentsService.getComments.and.returnValue(
        of(envelope(page([makeComment({ id: 1 })], { totalCount: 3, totalPages: 3 }))),
      );
      fixture.detectChanges();
    });

    it('appends the next page rather than replacing the list', () => {
      commentsService.getComments.and.returnValue(
        of(envelope(page([makeComment({ id: 2 })], { totalCount: 3, totalPages: 3 }))),
      );

      component.loadMore();

      expect(commentsService.getComments).toHaveBeenCalledWith(3, 9, 2, 20);
      expect(component.comments.map((c) => c.id)).toEqual([1, 2]);
      expect(component.loadingMore).toBeFalse();
    });

    it('does nothing on the last page', () => {
      component.totalPages = 1;
      commentsService.getComments.calls.reset();

      component.loadMore();

      expect(commentsService.getComments).not.toHaveBeenCalled();
    });

    it('does nothing while a page is already in flight', () => {
      commentsService.getComments.and.returnValue(
        new Subject<PostCommentsApiResponse>().asObservable(),
      );
      component.loadMore();
      commentsService.getComments.calls.reset();

      component.loadMore();

      expect(commentsService.getComments).not.toHaveBeenCalled();
    });
  });

  describe('submitComment', () => {
    beforeEach(() => fixture.detectChanges());

    it('prepends the created comment and clears the draft', () => {
      commentsService.createComment.and.returnValue(
        of(envelope(makeComment({ id: 2, content: 'Fresh' }))),
      );
      component.newCommentText = '  Fresh  ';

      component.submitComment();

      expect(commentsService.createComment).toHaveBeenCalledOnceWith(3, 9, 'Fresh');
      expect(component.comments[0].id).toBe(2);
      expect(component.totalCount).toBe(2);
      expect(component.newCommentText).toBe('');
      expect(component.submitting).toBeFalse();
    });

    it('ignores a blank draft', () => {
      component.newCommentText = '   ';

      component.submitComment();

      expect(commentsService.createComment).not.toHaveBeenCalled();
    });

    it('keeps the draft when posting fails', () => {
      commentsService.createComment.and.returnValue(
        throwError(() => ({ error: { Message: 'Comment rejected.' } })),
      );
      component.newCommentText = 'Fresh';

      component.submitComment();

      expect(component.submitError).toBe('Comment rejected.');
      expect(component.newCommentText).toBe('Fresh');
      expect(component.submitting).toBeFalse();
    });
  });

  describe('editing', () => {
    beforeEach(() => fixture.detectChanges());

    it('opens the editor pre-filled with the current text', () => {
      component.startEdit(makeComment({ id: 1, content: 'Nice one' }));

      expect(component.editingId).toBe(1);
      expect(component.editText).toBe('Nice one');
    });

    it('replaces the comment in place on save', () => {
      commentsService.updateComment.and.returnValue(
        of(envelope(makeComment({ id: 1, content: 'Edited' }))),
      );
      component.startEdit(component.comments[0]);
      component.editText = 'Edited';

      component.saveEdit(component.comments[0]);

      expect(commentsService.updateComment).toHaveBeenCalledOnceWith(3, 9, 1, 'Edited');
      expect(component.comments[0].content).toBe('Edited');
      expect(component.editingId).toBeNull();
    });

    it('closes the editor without a request when the text is unchanged', () => {
      const comment = component.comments[0];
      component.startEdit(comment);

      component.saveEdit(comment);

      expect(commentsService.updateComment).not.toHaveBeenCalled();
      expect(component.editingId).toBeNull();
    });

    it('closes the editor without a request when the text is blanked', () => {
      component.startEdit(component.comments[0]);
      component.editText = '   ';

      component.saveEdit(component.comments[0]);

      expect(commentsService.updateComment).not.toHaveBeenCalled();
    });

    it('keeps the editor open when saving fails', () => {
      commentsService.updateComment.and.returnValue(
        throwError(() => ({ error: { message: 'Edit rejected.' } })),
      );
      component.startEdit(component.comments[0]);
      component.editText = 'Edited';

      component.saveEdit(component.comments[0]);

      expect(component.submitError).toBe('Edit rejected.');
      expect(component.editingId).toBe(1);
    });

    it('discards the draft on cancel', () => {
      component.startEdit(component.comments[0]);

      component.cancelEdit();

      expect(component.editingId).toBeNull();
      expect(component.editText).toBe('');
    });
  });

  describe('deleting', () => {
    beforeEach(() => fixture.detectChanges());

    it('arms and disarms the confirmation', () => {
      component.confirmDelete(1);
      expect(component.deletingId).toBe(1);

      component.cancelDelete();
      expect(component.deletingId).toBeNull();
    });

    it('removes the comment and decrements the count', () => {
      commentsService.deleteComment.and.returnValue(of(envelope(null)));
      component.confirmDelete(1);

      component.deleteComment(1);

      expect(commentsService.deleteComment).toHaveBeenCalledOnceWith(3, 9, 1);
      expect(component.comments).toEqual([]);
      expect(component.totalCount).toBe(0);
      expect(component.deletingId).toBeNull();
    });

    it('disarms the confirmation and reports a failed delete', () => {
      commentsService.deleteComment.and.returnValue(
        throwError(() => ({ error: { message: 'Delete rejected.' } })),
      );
      component.confirmDelete(1);

      component.deleteComment(1);

      expect(component.submitError).toBe('Delete rejected.');
      expect(component.comments.length).toBe(1);
      expect(component.deletingId).toBeNull();
    });
  });

  describe('live updates', () => {
    beforeEach(() => fixture.detectChanges());

    it('prepends a comment created by someone else', () => {
      sse.next({ type: 'CommentCreated', comment: makeComment({ id: 2, userId: 5 }) });

      expect(component.comments.map((c) => c.id)).toEqual([2, 1]);
      expect(component.totalCount).toBe(2);
    });

    it('does not duplicate a comment this client already inserted optimistically', () => {
      commentsService.createComment.and.returnValue(of(envelope(makeComment({ id: 2 }))));
      component.newCommentText = 'Fresh';
      component.submitComment();

      sse.next({ type: 'CommentCreated', comment: makeComment({ id: 2 }) });

      expect(component.comments.filter((c) => c.id === 2).length).toBe(1);
      expect(component.totalCount).toBe(2);
    });

    it('replaces an edited comment in place', () => {
      sse.next({ type: 'CommentUpdated', comment: makeComment({ id: 1, content: 'Remote edit' }) });

      expect(component.comments[0].content).toBe('Remote edit');
      expect(component.totalCount).toBe(1);
    });

    it('removes a deleted comment and decrements the count', () => {
      sse.next({ type: 'CommentDeleted', postId: 9, commentId: 1 });

      expect(component.comments).toEqual([]);
      expect(component.totalCount).toBe(0);
    });

    it('ignores a delete for a comment that is not loaded', () => {
      sse.next({ type: 'CommentDeleted', postId: 9, commentId: 99 });

      expect(component.comments.length).toBe(1);
      expect(component.totalCount).toBe(1);
    });

    it('stops applying events after the component is destroyed', () => {
      component.ngOnDestroy();

      sse.next({ type: 'CommentCreated', comment: makeComment({ id: 2 }) });

      expect(component.comments.length).toBe(1);
    });
  });

  describe('display helpers', () => {
    beforeEach(() => fixture.detectChanges());

    it('recognises the signed-in user’s own comments', () => {
      expect(component.isOwnComment(makeComment({ userId: 1 }))).toBeTrue();
      expect(component.isOwnComment(makeComment({ userId: 5 }))).toBeFalse();
    });

    it('prefers the name, then the username, then the user id', () => {
      expect(component.authorDisplay(makeComment())).toBe('Test Member');
      expect(
        component.authorDisplay(
          makeComment({ author: { id: 1, name: null, username: 'member', avatar: null } }),
        ),
      ).toBe('member');
      expect(component.authorDisplay(makeComment({ author: null, userId: 42 }))).toBe('User #42');
    });

    it('builds up to two initials, falling back to a question mark', () => {
      expect(component.authorInitials(makeComment())).toBe('TM');
      expect(component.authorInitials(makeComment({ author: null }))).toBe('?');
    });

    it('describes recent timestamps relatively and old ones absolutely', () => {
      const now = Date.now();
      const ago = (ms: number) => new Date(now - ms).toISOString();

      expect(component.formatDate(ago(30_000))).toBe('just now');
      expect(component.formatDate(ago(5 * 60_000))).toBe('5m ago');
      expect(component.formatDate(ago(3 * 3_600_000))).toBe('3h ago');
      expect(component.formatDate(ago(2 * 86_400_000))).toBe('2d ago');
      expect(component.formatDate(ago(30 * 86_400_000))).toMatch(/\d{4}/);
    });
  });
});
