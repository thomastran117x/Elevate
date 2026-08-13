import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import { ClubDiscussionsService } from './club-discussions.service';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  ApiClientClientError,
  ApiClientServerError,
} from '../../../core/api/models/api-client-error.model';

describe('ClubDiscussionsService', () => {
  const base = `${environment.backendUrl}/clubs/3/discussions`;
  let service: ClubDiscussionsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubDiscussionsService, [ApiClient]));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('defaults to the first page of twenty', () => {
    service.getDiscussions(3).subscribe();

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });

  it('leaves a null data payload null', () => {
    let data: unknown = 'unset';
    service.getDiscussions(3).subscribe((response) => (data = response.data));

    httpMock.expectOne((req) => req.url === base).flush(envelope(null));

    expect(data).toBeNull();
  });

  it('normalizes a PascalCase paged payload', () => {
    let data: unknown;
    service.getDiscussions(3, 2, 5).subscribe((response) => (data = response.data));

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('5');
    request.flush(
      pascalEnvelope({
        Items: [{ Id: 1, Title: 'Weekend ride', Description: 'Where?' }],
        TotalCount: 1,
        Page: 2,
      }),
    );

    expect(data).toEqual(
      jasmine.objectContaining({
        page: 2,
        items: [jasmine.objectContaining({ id: 1, title: 'Weekend ride', description: 'Where?' })],
      }),
    );
  });

  it('normalizes the discussion returned by create and update', () => {
    let created: unknown;
    service
      .createDiscussion(3, { title: 'Weekend ride', description: 'Where?' })
      .subscribe((r) => (created = r.data));

    const post = httpMock.expectOne(base);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ title: 'Weekend ride', description: 'Where?' });
    expect(post.request.withCredentials).toBeTrue();
    post.flush(pascalEnvelope({ Id: 1, Title: 'Weekend ride', Description: 'Where?' }));

    expect(created).toEqual(jasmine.objectContaining({ id: 1, title: 'Weekend ride' }));

    let updated: unknown;
    service
      .updateDiscussion(3, 1, { title: 'Sunday ride', description: 'Sunday works better.' })
      .subscribe((r) => (updated = r.data));

    const put = httpMock.expectOne(`${base}/1`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({
      title: 'Sunday ride',
      description: 'Sunday works better.',
    });
    put.flush(pascalEnvelope({ Id: 1, Title: 'Sunday ride', Description: 'Sunday works better.' }));

    expect(updated).toEqual(
      jasmine.objectContaining({ title: 'Sunday ride', description: 'Sunday works better.' }),
    );
  });

  it('leaves a null create payload null', () => {
    let created: unknown = 'unset';
    service
      .createDiscussion(3, { title: 'A', description: 'B' })
      .subscribe((r) => (created = r.data));

    httpMock.expectOne(base).flush(envelope(null));

    expect(created).toBeNull();
  });

  it('deletes a discussion', () => {
    service.deleteDiscussion(3, 1).subscribe();

    const request = httpMock.expectOne(`${base}/1`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });

  it('surfaces a 4xx as a typed client error', () => {
    let thrown: unknown;
    service
      .createDiscussion(3, { title: '', description: '' })
      .subscribe({ error: (e) => (thrown = e) });

    httpMock
      .expectOne(base)
      .flush(
        errorEnvelope('FORBIDDEN', 'You must be a member of this club to start a discussion.'),
        {
          status: 403,
          statusText: 'Forbidden',
        },
      );

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe(
      'You must be a member of this club to start a discussion.',
    );
    expect((thrown as ApiClientClientError).code).toBe('FORBIDDEN');
  });

  it('surfaces a 5xx as a typed server error', () => {
    let thrown: unknown;
    service.getDiscussions(3).subscribe({ error: (e) => (thrown = e) });

    httpMock
      .expectOne((req) => req.url === base)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
  });
});
