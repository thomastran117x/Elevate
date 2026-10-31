import { HttpTestingController } from '@angular/common/http/testing';
import { fakeAsync, tick } from '@angular/core/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import {
  SecurityEventsPage,
  SecurityHistoryService,
  normalizeSecurityEventsPage,
} from './security-history.service';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { ApiClientClientError } from '../../../core/api/models/api-client-error.model';

describe('SecurityHistoryService', () => {
  const url = `${environment.backendUrl}/auth/security-events`;
  const entry = {
    id: 7,
    kind: 'password_changed',
    occurredAt: '2026-10-20T12:00:00Z',
    method: null,
    factor: 'password',
    reason: null,
    client: 'chrome',
    deviceClass: 'desktop',
    network: '203.0.113.0/24',
    initiatedByAdmin: false,
    reference: 'req-7',
  };

  let service: SecurityHistoryService;
  let httpMock: HttpTestingController;
  let ensureCsrfToken: jasmine.Spy<() => Promise<void>>;

  beforeEach(() => {
    ensureCsrfToken = jasmine.createSpy('ensureCsrfToken').and.resolveTo();

    ({ service, httpMock } = setupService(SecurityHistoryService, [
      ApiClient,
      { provide: AuthTokenService, useValue: { ensureCsrfToken } },
    ]));
  });

  afterEach(() => httpMock.verify());

  it('requests the page with credentials after ensuring a CSRF token', fakeAsync(() => {
    let page: SecurityEventsPage | undefined;
    service.getSecurityEvents(2, 10).subscribe((value) => (page = value));
    tick();

    const request = httpMock.expectOne((req) => req.url === url);
    expect(ensureCsrfToken).toHaveBeenCalled();
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush(
      envelope({ items: [entry], totalCount: 11, page: 2, pageSize: 10, totalPages: 2 }),
    );

    expect(page).toEqual({ items: [entry], totalCount: 11, page: 2, pageSize: 10, totalPages: 2 });
  }));

  it('keeps paging inside the bounds the API accepts', fakeAsync(() => {
    service.getSecurityEvents(0, 500).subscribe();
    tick();

    const request = httpMock.expectOne((req) => req.url === url);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0 }));
  }));

  it('never asks for a page past the furthest the API accepts', fakeAsync(() => {
    service.getSecurityEvents(900, 10).subscribe();
    tick();

    const request = httpMock.expectOne((req) => req.url === url);
    expect(request.request.params.get('page')).toBe('500');
    request.flush(envelope({ items: [], totalCount: 0, page: 500, pageSize: 10, totalPages: 0 }));
  }));

  it('reads a PascalCase payload', fakeAsync(() => {
    let page: SecurityEventsPage | undefined;
    service.getSecurityEvents().subscribe((value) => (page = value));
    tick();

    httpMock
      .expectOne((req) => req.url === url)
      .flush(
        pascalEnvelope({
          Items: [
            {
              Id: 3,
              Kind: 'signed_in',
              OccurredAt: '2026-10-19T08:00:00Z',
              Method: 'google',
              Client: 'firefox',
              DeviceClass: 'mobile',
              InitiatedByAdmin: true,
            },
          ],
          TotalCount: 1,
          Page: 1,
          PageSize: 20,
          TotalPages: 1,
        }),
      );

    expect(page?.items[0]).toEqual({
      id: 3,
      kind: 'signed_in',
      occurredAt: '2026-10-19T08:00:00Z',
      method: 'google',
      factor: null,
      reason: null,
      client: 'firefox',
      deviceClass: 'mobile',
      network: null,
      initiatedByAdmin: true,
      reference: null,
    });
  }));

  it('fails when the envelope carries no page', fakeAsync(() => {
    let error: unknown;
    service.getSecurityEvents().subscribe({ error: (err) => (error = err) });
    tick();

    httpMock.expectOne((req) => req.url === url).flush(envelope('not a page'));

    expect((error as Error).message).toBe('Security history response was incomplete.');
  }));

  it('surfaces API errors', fakeAsync(() => {
    let error: unknown;
    service.getSecurityEvents().subscribe({ error: (err) => (error = err) });
    tick();

    httpMock
      .expectOne((req) => req.url === url)
      .flush(errorEnvelope('UNAUTHORIZED', 'Sign in again.'), {
        status: 401,
        statusText: 'Unauthorized',
      });

    expect(error).toEqual(jasmine.any(ApiClientClientError));
  }));
});

describe('normalizeSecurityEventsPage', () => {
  it('returns null for something that is not an object', () => {
    expect(normalizeSecurityEventsPage(null)).toBeNull();
    expect(normalizeSecurityEventsPage('page')).toBeNull();
  });

  it('drops entries missing the fields a row needs, and defaults the rest', () => {
    const page = normalizeSecurityEventsPage({
      items: [
        { id: 1, kind: 'signed_out', occurredAt: '2026-10-20T12:00:00Z' },
        { kind: 'signed_out', occurredAt: '2026-10-20T12:00:00Z' },
        { id: 2, occurredAt: '2026-10-20T12:00:00Z' },
        { id: 3, kind: 'signed_out' },
        'not an entry',
      ],
    });

    expect(page?.items.map((item) => item.id)).toEqual([1]);
    expect(page?.items[0].client).toBe('unknown');
    expect(page?.items[0].deviceClass).toBe('unknown');
    expect(page?.items[0].initiatedByAdmin).toBeFalse();
    expect(page?.page).toBe(1);
    expect(page?.totalCount).toBe(1);
    expect(page?.pageSize).toBe(1);
    expect(page?.totalPages).toBe(1);
  });

  it('derives the page count when the payload omits it', () => {
    expect(
      normalizeSecurityEventsPage({ items: [], totalCount: 21, pageSize: 10 })?.totalPages,
    ).toBe(3);
    expect(normalizeSecurityEventsPage({ items: 'nope', totalCount: 0, pageSize: 0 })).toEqual({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 0,
      totalPages: 0,
    });
  });
});
