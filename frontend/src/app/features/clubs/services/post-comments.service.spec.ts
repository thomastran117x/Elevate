import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { PostCommentsService } from './post-comments.service';

describe('PostCommentsService', () => {
  const base = `${environment.backendUrl}/clubs/3/posts/7/comments`;
  let service: PostCommentsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(PostCommentsService, [ApiClient]));
  });

  afterEach(() => httpMock.verify());

  it('loads one direct level with sort and cursor parameters', () => {
    let data: unknown;
    service.getComments(3, 7, 4, 'Oldest', 'next', 5).subscribe((r) => (data = r.data));

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('parentCommentId')).toBe('4');
    expect(request.request.params.get('sort')).toBe('Oldest');
    expect(request.request.params.get('cursor')).toBe('next');
    expect(request.request.params.get('pageSize')).toBe('5');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(
      pascalEnvelope({ Items: [{ Id: 1, Content: 'Reply' }], TotalCount: 1, HasMore: false }),
    );
    expect(data).toEqual(
      jasmine.objectContaining({ items: [jasmine.objectContaining({ id: 1, content: 'Reply' })] }),
    );
  });

  it('creates and edits nested replies', () => {
    service.createComment(3, 7, 'Child', 4).subscribe();
    const create = httpMock.expectOne(base);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ content: 'Child', parentCommentId: 4 });
    create.flush(envelope({ id: 5 }));

    service.updateComment(3, 7, 5, 'Edited').subscribe();
    const update = httpMock.expectOne(`${base}/5`);
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({ content: 'Edited' });
    update.flush(envelope({ id: 5, content: 'Edited' }));
  });

  it('sets, switches, and clears an exclusive reaction through the reaction resource', () => {
    service.setReaction(3, 7, 5, 'Dislike').subscribe();
    const set = httpMock.expectOne(`${base}/5/reaction`);
    expect(set.request.method).toBe('PUT');
    expect(set.request.body).toEqual({ reaction: 'Dislike' });
    set.flush(envelope({ commentId: 5, dislikeCount: 1, currentUserReaction: 'Dislike' }));

    service.clearReaction(3, 7, 5).subscribe();
    const clear = httpMock.expectOne(`${base}/5/reaction`);
    expect(clear.request.method).toBe('DELETE');
    clear.flush(envelope({ commentId: 5, dislikeCount: 0, currentUserReaction: null }));
  });

  it('deletes a comment, with a moderation reason when one is given', () => {
    service.deleteComment(3, 7, 5).subscribe();
    const own = httpMock.expectOne(`${base}/5`);
    expect(own.request.method).toBe('DELETE');
    expect(own.request.params.has('reason')).toBeFalse();
    own.flush(envelope({ id: 5, isDeleted: true }));

    service.deleteComment(3, 7, 5, 'Spam').subscribe();
    const moderated = httpMock.expectOne((req) => req.url === `${base}/5`);
    expect(moderated.request.params.get('reason')).toBe('Spam');
    moderated.flush(envelope({ id: 5, isDeleted: true }));
  });
});
