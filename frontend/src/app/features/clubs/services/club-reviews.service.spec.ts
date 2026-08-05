import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import { ClubReviewsService } from './club-reviews.service';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  ApiClientClientError,
  ApiClientServerError,
} from '../../../core/api/models/api-client-error.model';

describe('ClubReviewsService', () => {
  const base = `${environment.backendUrl}/clubs/3/reviews`;
  let service: ClubReviewsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubReviewsService, [ApiClient]));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('defaults to the first page of twenty', () => {
    service.getReviews(3).subscribe();

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });

  it('normalizes a PascalCase paged payload', () => {
    let data: unknown;
    service.getReviews(3, 2, 5).subscribe((response) => (data = response.data));

    const request = httpMock.expectOne((req) => req.url === base);
    expect(request.request.params.get('page')).toBe('2');
    request.flush(
      pascalEnvelope({ Items: [{ Id: 1, Rating: 5, Title: 'Great' }], TotalCount: 1, Page: 2 }),
    );

    expect(data).toEqual(
      jasmine.objectContaining({
        page: 2,
        items: [jasmine.objectContaining({ id: 1, rating: 5, title: 'Great' })],
      }),
    );
  });

  it('normalizes the review returned by create and update', () => {
    let created: unknown;
    service
      .createReview(3, { title: 'Great', rating: 5, comment: 'Welcoming' })
      .subscribe((r) => (created = r.data));

    const post = httpMock.expectOne(base);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ title: 'Great', rating: 5, comment: 'Welcoming' });
    post.flush(pascalEnvelope({ Id: 1, Rating: 5, Title: 'Great' }));

    expect(created).toEqual(jasmine.objectContaining({ id: 1, rating: 5 }));

    let updated: unknown;
    service.updateReview(3, 1, { title: 'Good', rating: 4 }).subscribe((r) => (updated = r.data));

    const put = httpMock.expectOne(`${base}/1`);
    expect(put.request.method).toBe('PUT');
    put.flush(pascalEnvelope({ Id: 1, Rating: 4, Title: 'Good' }));

    expect(updated).toEqual(jasmine.objectContaining({ rating: 4, title: 'Good' }));
  });

  it('deletes a review', () => {
    service.deleteReview(3, 1).subscribe();

    const request = httpMock.expectOne(`${base}/1`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));
  });

  it('surfaces a 4xx as a typed client error', () => {
    let thrown: unknown;
    service.createReview(3, { title: 'x', rating: 6 }).subscribe({ error: (e) => (thrown = e) });

    httpMock
      .expectOne(base)
      .flush(errorEnvelope('VALIDATION_FAILED', 'Rating must be between 1 and 5.'), {
        status: 400,
        statusText: 'Bad Request',
      });

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe('Rating must be between 1 and 5.');
    expect((thrown as ApiClientClientError).code).toBe('VALIDATION_FAILED');
  });

  it('surfaces a 5xx as a typed server error', () => {
    let thrown: unknown;
    service.getReviews(3).subscribe({ error: (e) => (thrown = e) });

    httpMock
      .expectOne((req) => req.url === base)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
  });
});
