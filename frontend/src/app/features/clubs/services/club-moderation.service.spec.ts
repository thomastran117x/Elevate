import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { ClubModerationService } from './club-moderation.service';

describe('ClubModerationService', () => {
  const url = `${environment.backendUrl}/clubs/3/moderation-log`;
  let service: ClubModerationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubModerationService, [ApiClient]));
  });

  afterEach(() => httpMock.verify());

  it('requests a page of the log with credentials and normalizes it', () => {
    let data: unknown;
    service.getModerationLog(3, 2, 10).subscribe((response) => (data = response.data));

    const request = httpMock.expectOne((req) => req.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush(
      pascalEnvelope({
        Items: [{ Id: 1, Action: 'pin', ContentType: 'Post', Actor: { Id: 4 } }],
        TotalCount: 1,
        Page: 2,
        PageSize: 10,
        TotalPages: 1,
      }),
    );

    expect(data).toEqual(
      jasmine.objectContaining({
        totalCount: 1,
        items: [jasmine.objectContaining({ id: 1, action: 'pin', contentType: 'Post' })],
      }),
    );
  });

  it('leaves the data empty when the server sends none', () => {
    let data: unknown = 'unset';
    service.getModerationLog(3).subscribe((response) => (data = response.data));

    httpMock.expectOne((req) => req.url === url).flush(envelope(null));

    expect(data).toBeNull();
  });
});
