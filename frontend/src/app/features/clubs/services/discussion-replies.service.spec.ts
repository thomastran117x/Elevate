import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { DiscussionRepliesService } from './discussion-replies.service';

describe('DiscussionRepliesService', () => {
  const base = `${environment.backendUrl}/clubs/3/discussions/7/replies`;
  let service: DiscussionRepliesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(DiscussionRepliesService, [ApiClient]));
  });

  afterEach(() => httpMock.verify());

  it('loads one direct level with sort and cursor parameters', () => {
    let data: unknown;
    service.getReplies(3, 7, 4, 'Oldest', 'next', 5).subscribe((r) => (data = r.data));

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('parentReplyId')).toBe('4');
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
    service.createReply(3, 7, 'Child', 4).subscribe();
    const create = httpMock.expectOne(base);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ content: 'Child', parentReplyId: 4 });
    create.flush(envelope({ id: 5 }));

    service.updateReply(3, 7, 5, 'Edited').subscribe();
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
    set.flush(envelope({ replyId: 5, dislikeCount: 1, currentUserReaction: 'Dislike' }));

    service.clearReaction(3, 7, 5).subscribe();
    const clear = httpMock.expectOne(`${base}/5/reaction`);
    expect(clear.request.method).toBe('DELETE');
    clear.flush(envelope({ replyId: 5, dislikeCount: 0, currentUserReaction: null }));
  });
});
