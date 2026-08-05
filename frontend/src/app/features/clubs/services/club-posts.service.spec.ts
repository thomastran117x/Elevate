import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';

import { ClubPostsService } from './club-posts.service';

describe('ClubPostsService', () => {
  const base = `${environment.backendUrl}/clubs/3/posts`;
  let service: ClubPostsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubPostsService));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('serializes only the search filters that are set', () => {
    service
      .getPosts(3, { search: '  kickoff  ', sortBy: 'Popular', page: 2, pageSize: 5 })
      .subscribe();

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('search')).toBe('kickoff');
    expect(request.request.params.get('sortBy')).toBe('Popular');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('5');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });

  it('omits blank and zero filters', () => {
    service.getPosts(3, { search: '   ', page: 0, pageSize: 0 }).subscribe();

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.keys()).toEqual([]);
    request.flush(envelope(null));
  });

  it('normalizes a PascalCase paged payload', () => {
    let data: unknown;
    service.getPosts(3).subscribe((response) => (data = response.data));

    httpMock
      .expectOne((req) => req.url === base)
      .flush(
        pascalEnvelope({
          Items: [{ Id: 1, Title: 'Kickoff', PostType: 1 }],
          TotalCount: 1,
          Page: 1,
          PageSize: 20,
          TotalPages: 1,
        }),
      );

    expect(data).toEqual(
      jasmine.objectContaining({
        totalCount: 1,
        items: [jasmine.objectContaining({ id: 1, title: 'Kickoff', postType: 'Announcement' })],
      }),
    );
  });

  it('leaves data null when the envelope carries none', () => {
    let data: unknown = 'untouched';
    service.getPosts(3).subscribe((response) => (data = response.data));

    httpMock.expectOne((req) => req.url === base).flush(envelope(null));

    expect(data).toBeNull();
  });

  it('normalizes a single post on read, create and update', () => {
    const results: unknown[] = [];

    service.getPost(3, 9).subscribe((r) => results.push(r.data));
    httpMock.expectOne(`${base}/9`).flush(pascalEnvelope({ Id: 9, Title: 'Read' }));

    service
      .createPost(3, { title: 'New', content: 'x', postType: 'General', isPinned: false })
      .subscribe((r) => results.push(r.data));
    const created = httpMock.expectOne(base);
    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual({
      title: 'New',
      content: 'x',
      postType: 'General',
      isPinned: false,
    });
    created.flush(pascalEnvelope({ Id: 10, Title: 'New' }));

    service
      .updatePost(3, 9, { title: 'Edited', content: 'y', postType: 'Poll', isPinned: true })
      .subscribe((r) => results.push(r.data));
    const updated = httpMock.expectOne(`${base}/9`);
    expect(updated.request.method).toBe('PUT');
    updated.flush(pascalEnvelope({ Id: 9, Title: 'Edited', IsPinned: true }));

    expect(results.map((r) => (r as { title: string }).title)).toEqual(['Read', 'New', 'Edited']);
    expect((results[2] as { isPinned: boolean }).isPinned).toBeTrue();
  });

  it('deletes a post with credentials', () => {
    service.deletePost(3, 9).subscribe();

    const request = httpMock.expectOne(`${base}/9`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });
});
