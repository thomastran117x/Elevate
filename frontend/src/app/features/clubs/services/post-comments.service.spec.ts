import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';

import { PostCommentsService } from './post-comments.service';

describe('PostCommentsService', () => {
  const base = `${environment.backendUrl}/clubs/3/posts/9/comments`;
  let service: PostCommentsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(PostCommentsService));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('defaults to the first page of twenty', () => {
    service.getComments(3, 9).subscribe();

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush(envelope(null));
  });

  it('normalizes a PascalCase paged payload including nested authors', () => {
    let data: unknown;
    service.getComments(3, 9, 2, 5).subscribe((response) => (data = response.data));

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('page')).toBe('2');
    request.flush(
      pascalEnvelope({
        Items: [{ Id: 1, Content: 'Nice', Author: { Id: 7, Username: 'jamie' } }],
        TotalCount: 1,
      }),
    );

    expect(data).toEqual(
      jasmine.objectContaining({
        items: [
          jasmine.objectContaining({
            id: 1,
            content: 'Nice',
            author: { id: 7, name: null, username: 'jamie', avatar: null },
          }),
        ],
      }),
    );
  });

  it('posts the comment body and normalizes the created comment', () => {
    let data: unknown;
    service.createComment(3, 9, 'Nice one').subscribe((response) => (data = response.data));

    const request = httpMock.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ content: 'Nice one' });
    expect(request.request.withCredentials).toBeTrue();
    request.flush(pascalEnvelope({ Id: 5, Content: 'Nice one', PostId: 9 }));

    expect(data).toEqual(jasmine.objectContaining({ id: 5, postId: 9, content: 'Nice one' }));
  });

  it('puts the edited body to the comment URL', () => {
    let data: unknown;
    service.updateComment(3, 9, 5, 'Edited').subscribe((response) => (data = response.data));

    const request = httpMock.expectOne(`${base}/5`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ content: 'Edited' });
    request.flush(pascalEnvelope({ Id: 5, Content: 'Edited' }));

    expect(data).toEqual(jasmine.objectContaining({ content: 'Edited' }));
  });

  it('deletes a comment with credentials', () => {
    service.deleteComment(3, 9, 5).subscribe();

    const request = httpMock.expectOne(`${base}/5`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });

  it('leaves data null when the mutation returns no payload', () => {
    let data: unknown = 'untouched';
    service.createComment(3, 9, 'x').subscribe((response) => (data = response.data));

    httpMock.expectOne(base).flush(envelope(null));

    expect(data).toBeNull();
  });
});
