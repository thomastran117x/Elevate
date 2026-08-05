import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { MockStore } from '@ngrx/store/testing';

import { environment } from '@environments/environment';
import { dispatchSpy, envelope, makeSession, provideTestStore, setupService } from '@testing';

import { AuthTokenService } from './auth-token.service';
import { ApiClient } from './api-client.service';
import { clearSession, updateSession } from '../../stores/session.actions';
import { clearUser } from '../../stores/user.actions';
import { selectAccessToken } from '../../stores/session.selectors';

describe('AuthTokenService', () => {
  const backendUrl = environment.backendUrl;
  let service: AuthTokenService;
  let httpMock: ReturnType<typeof setupService<AuthTokenService>>['httpMock'];
  let store: MockStore;
  let dispatch: jasmine.Spy;

  beforeEach(() => {
    const harness = setupService(AuthTokenService, [ApiClient, ...provideTestStore()]);
    service = harness.service;
    httpMock = harness.httpMock;
    store = TestBed.inject(Store) as MockStore;
    dispatch = dispatchSpy(store);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushCsrf(token = 'csrf-123'): void {
    httpMock.expectOne(`${backendUrl}/auth/csrf`).flush(envelope({ token }));
  }

  describe('ensureCsrfToken', () => {
    it('fetches the token with credentials and stores it', fakeAsync(() => {
      service.ensureCsrfToken();

      const request = httpMock.expectOne(`${backendUrl}/auth/csrf`);
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBeTrue();
      request.flush(envelope({ token: 'csrf-123' }));
      tick();

      expect(service.csrfToken).toBe('csrf-123');
    }));

    it('short-circuits when a token is already held', fakeAsync(() => {
      service.csrfToken = 'already-here';

      service.ensureCsrfToken();
      tick();

      httpMock.expectNone(`${backendUrl}/auth/csrf`);
      expect<string | null>(service.csrfToken).toBe('already-here');
    }));

    it('deduplicates concurrent callers into a single request', fakeAsync(() => {
      service.ensureCsrfToken();
      service.ensureCsrfToken();
      service.ensureCsrfToken();

      flushCsrf();
      tick();

      expect(service.csrfToken).toBe('csrf-123');
    }));

    it('releases the in-flight slot so a later call can re-fetch', fakeAsync(() => {
      service.ensureCsrfToken();
      flushCsrf('first');
      tick();

      service.csrfToken = null;
      service.ensureCsrfToken();
      flushCsrf('second');
      tick();

      expect<string | null>(service.csrfToken).toBe('second');
    }));

    it('leaves the token null when the response carries no payload', fakeAsync(() => {
      service.ensureCsrfToken();

      httpMock.expectOne(`${backendUrl}/auth/csrf`).flush(envelope(null));
      tick();

      expect(service.csrfToken).toBeNull();
    }));
  });

  describe('refreshAccessToken', () => {
    it('bootstraps CSRF first, then dispatches the new session', fakeAsync(() => {
      service.refreshAccessToken();

      flushCsrf();
      tick();

      const refresh = httpMock.expectOne(`${backendUrl}/auth/refresh`);
      expect(refresh.request.method).toBe('POST');
      expect(refresh.request.withCredentials).toBeTrue();
      refresh.flush(envelope({ AccessToken: 'fresh-token', ExpiresAtUtc: '2026-09-01T00:00:00Z' }));
      tick();

      expect(service.accessToken).toBe('fresh-token');
      expect(dispatch).toHaveBeenCalledWith(
        updateSession({ accessToken: 'fresh-token', expiresAtUtc: '2026-09-01T00:00:00Z' }),
      );
    }));

    it('accepts a bare (non-enveloped) camelCase response', fakeAsync(() => {
      service.csrfToken = 'csrf-123';
      service.refreshAccessToken();
      tick(); // the short-circuited ensureCsrfToken still resolves on a microtask

      httpMock.expectOne(`${backendUrl}/auth/refresh`).flush({
        accessToken: 'fresh-token',
        expiresAtUtc: '2026-09-01T00:00:00Z',
      });
      tick();

      expect(service.accessToken).toBe('fresh-token');
    }));

    it('rejects when the response carries no access token', fakeAsync(() => {
      service.csrfToken = 'csrf-123';

      let error: Error | undefined;
      service.refreshAccessToken().catch((err: Error) => (error = err));
      tick();

      httpMock.expectOne(`${backendUrl}/auth/refresh`).flush(envelope({ ExpiresAtUtc: 'x' }));
      tick();

      expect(error?.message).toBe('Refresh response did not include an access token.');
      expect(dispatch).not.toHaveBeenCalled();
    }));
  });

  describe('logoutLocal', () => {
    it('clears both slices of state and both tokens', () => {
      service.accessToken = 'token';
      service.csrfToken = 'csrf';

      service.logoutLocal();

      expect(dispatch).toHaveBeenCalledWith(clearUser());
      expect(dispatch).toHaveBeenCalledWith(clearSession());
      expect(service.accessToken).toBeNull();
      expect(service.csrfToken).toBeNull();
    });
  });

  it('tracks the access token emitted by the store', () => {
    store.overrideSelector(selectAccessToken, makeSession().AccessToken);
    store.refreshState();

    expect(service.accessToken).toBe('access-token');

    store.overrideSelector(selectAccessToken, null);
    store.refreshState();

    expect(service.accessToken).toBeNull();
  });
});
