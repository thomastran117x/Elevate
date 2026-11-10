import { HttpTestingController } from '@angular/common/http/testing';
import { fakeAsync, tick } from '@angular/core/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import { ApiClientClientError } from '../../../core/api/models/api-client-error.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  isLinkableProvider,
  normalizeEmailLinkProposal,
  normalizeProviderLink,
  normalizeProviderLinksOverview,
  normalizeProviderLinkStart,
  PROVIDER_LINK_EXPIRED_ERROR_CODE,
  ProviderLink,
  ProviderLinksOverview,
  ProviderLinksService,
} from './provider-links.service';

describe('ProviderLinksService', () => {
  const url = `${environment.backendUrl}/auth/providers`;
  const link = {
    provider: 'google',
    email: 'owner@gmail.com',
    displayName: 'Owner',
    linkedAtUtc: '2026-11-01T09:00:00Z',
    lastUsedAtUtc: null,
  };

  let service: ProviderLinksService;
  let httpMock: HttpTestingController;
  let ensureCsrfToken: jasmine.Spy<() => Promise<void>>;

  beforeEach(() => {
    ensureCsrfToken = jasmine.createSpy('ensureCsrfToken').and.resolveTo();
    ({ service, httpMock } = setupService(ProviderLinksService, [
      ApiClient,
      { provide: AuthTokenService, useValue: { ensureCsrfToken } },
    ]));
  });

  afterEach(() => httpMock.verify());

  it('lists the providers with credentials after ensuring a CSRF token', fakeAsync(() => {
    let overview: ProviderLinksOverview | undefined;
    service.getProviders().subscribe((value) => (overview = value));
    tick();

    const request = httpMock.expectOne(url);
    expect(ensureCsrfToken).toHaveBeenCalled();
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(
      envelope({
        hasPassword: false,
        providers: [
          { provider: 'google', available: true, link, canUnlink: false },
          { provider: 'microsoft', available: false, link: null, canUnlink: false },
        ],
      }),
    );

    expect(overview).toEqual({
      hasPassword: false,
      providers: [
        { provider: 'google', available: true, link: link as ProviderLink, canUnlink: false },
        { provider: 'microsoft', available: false, link: null, canUnlink: false },
      ],
    });
  }));

  it('starts a link and reads the server-issued state', fakeAsync(() => {
    let state: string | undefined;
    let verifier: string | null | undefined;
    service.startLink('microsoft').subscribe((start) => {
      state = start.state;
      verifier = start.codeVerifier;
    });
    tick();

    const request = httpMock.expectOne(`${url}/microsoft/link/start`);
    expect(request.request.method).toBe('POST');
    request.flush(
      envelope({
        authorizationUrl: 'https://login.example/authorize',
        state: 's',
        redirectUri: 'https://app.example/auth/microsoft',
        codeVerifier: 'v',
      }),
    );

    expect(state).toBe('s');
    expect(verifier).toBe('v');
  }));

  it('completes a link with what the callback returned', fakeAsync(() => {
    let linked: ProviderLink | undefined;
    service
      .completeLink('google', { state: 's', code: 'c' })
      .subscribe((value) => (linked = value));
    tick();

    const request = httpMock.expectOne(`${url}/google/link/complete`);
    expect(request.request.body).toEqual({ state: 's', code: 'c' });
    request.flush(pascalEnvelope({ Provider: 'google', LinkedAtUtc: '2026-11-01T09:00:00Z' }));

    expect(linked?.provider).toBe('google');
    expect(linked?.email).toBeNull();
  }));

  it('confirms an email-matched link by its token', fakeAsync(() => {
    service.confirmLink('link-token').subscribe();
    tick();

    const request = httpMock.expectOne(`${url}/link/confirm`);
    expect(request.request.body).toEqual({ linkToken: 'link-token' });
    request.flush(envelope(link));
  }));

  it('unlinks with a DELETE', fakeAsync(() => {
    let done = false;
    service.unlink('google').subscribe(() => (done = true));
    tick();

    const request = httpMock.expectOne(`${url}/google`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush({ success: true, message: 'Provider unlinked.', data: null });

    expect(done).toBeTrue();
  }));

  it('surfaces a refusal with its error code', fakeAsync(() => {
    let error: unknown;
    service.confirmLink('old-token').subscribe({ error: (err) => (error = err) });
    tick();

    httpMock
      .expectOne(`${url}/link/confirm`)
      .flush(errorEnvelope(PROVIDER_LINK_EXPIRED_ERROR_CODE, 'Start again.'), {
        status: 410,
        statusText: 'Gone',
      });

    expect(error).toEqual(jasmine.any(ApiClientClientError));
    expect((error as ApiClientClientError).code).toBe(PROVIDER_LINK_EXPIRED_ERROR_CODE);
  }));

  it('fails when a response is missing what the view needs', fakeAsync(() => {
    let error: unknown;
    service.startLink('google').subscribe({ error: (err) => (error = err) });
    tick();

    httpMock.expectOne(`${url}/google/link/start`).flush(envelope({ state: 's' }));

    expect((error as Error).message).toContain('incomplete');
  }));

  describe('normalizers', () => {
    it('keeps only providers the browser knows', () => {
      const overview = normalizeProviderLinksOverview({
        HasPassword: true,
        Providers: [
          { Provider: 'apple', Available: true },
          { Provider: 'google', Available: true, Link: null, CanUnlink: true },
          'not-an-object',
        ],
      });

      expect(overview?.hasPassword).toBeTrue();
      expect(overview?.providers.map((p) => p.provider)).toEqual(['google']);
      // Nothing to unlink without a link, whatever the flag says.
      expect(overview?.providers[0].canUnlink).toBeFalse();
    });

    it('rejects malformed payloads', () => {
      expect(normalizeProviderLinksOverview(null)).toBeNull();
      expect(normalizeProviderLinksOverview({ providers: 'x' })?.providers).toEqual([]);
      expect(normalizeProviderLink({ provider: 'google' })).toBeNull();
      expect(normalizeProviderLink('x')).toBeNull();
      expect(normalizeProviderLinkStart(null)).toBeNull();
      // Without the issued redirect the browser could not redeem a code the provider accepts.
      expect(
        normalizeProviderLinkStart({
          authorizationUrl: 'https://x',
          state: 's',
          codeVerifier: 'v',
        }),
      ).toBeNull();
      expect(normalizeEmailLinkProposal({ linkToken: 't', provider: 'apple' })).toBeNull();
      expect(normalizeEmailLinkProposal(undefined)).toBeNull();
    });

    it('reads a link-confirmation proposal in either casing', () => {
      expect(
        normalizeEmailLinkProposal({
          LinkToken: 't',
          Provider: 'microsoft',
          MaskedEmail: 'o***@example.com',
          ExpiresAtUtc: '2026-11-07T12:10:00Z',
        }),
      ).toEqual({
        linkToken: 't',
        provider: 'microsoft',
        maskedEmail: 'o***@example.com',
        expiresAtUtc: '2026-11-07T12:10:00Z',
      });
    });

    it('recognizes linkable providers only', () => {
      expect(isLinkableProvider('google')).toBeTrue();
      expect(isLinkableProvider('microsoft')).toBeTrue();
      expect(isLinkableProvider('apple')).toBeFalse();
      expect(isLinkableProvider(undefined)).toBeFalse();
    });
  });
});
