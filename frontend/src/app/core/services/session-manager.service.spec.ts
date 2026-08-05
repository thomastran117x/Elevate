import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { MockStore } from '@ngrx/store/testing';
import { of, throwError } from 'rxjs';

import { environment } from '@environments/environment';
import {
  dispatchSpy,
  envelope,
  flushPromises,
  makeCurrentUser,
  provideFeatureFlags,
  provideHttpTesting,
  provideTestStore,
} from '@testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { SessionManagerService } from './session-manager.service';
import { AuthTokenService } from '../api/services/auth-token.service';
import { AuthService } from '../../features/auth/services/auth.service';
import { setSession, clearSession } from '../stores/session.actions';
import { setUser, clearUser } from '../stores/user.actions';
import { FeatureFlags } from '../features/feature-flags.types';

describe('SessionManagerService', () => {
  const backendUrl = environment.backendUrl;
  const validSession = { AccessToken: 'token', ExpiresAtUtc: '2026-09-01T00:00:00Z' };

  let httpMock: HttpTestingController;
  let dispatch: jasmine.Spy;
  let authToken: {
    csrfToken: string | null;
    ensureCsrfToken: jasmine.Spy<() => Promise<void>>;
  };
  let authService: { me: jasmine.Spy };

  function createService(
    options: { platform?: string; flags?: FeatureFlags } = {},
  ): SessionManagerService {
    authToken = {
      csrfToken: 'csrf-123',
      ensureCsrfToken: jasmine.createSpy('ensureCsrfToken').and.resolveTo(),
    };
    authService = { me: jasmine.createSpy('me').and.returnValue(of(makeCurrentUser())) };

    TestBed.configureTestingModule({
      providers: [
        SessionManagerService,
        ...provideHttpTesting(),
        ...provideTestStore(),
        provideFeatureFlags(options.flags ?? {}),
        { provide: PLATFORM_ID, useValue: options.platform ?? 'browser' },
        { provide: AuthTokenService, useValue: authToken },
        { provide: AuthService, useValue: authService },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    dispatch = dispatchSpy(TestBed.inject(Store) as MockStore);

    return TestBed.inject(SessionManagerService);
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  describe('restoreSession', () => {
    it('issues no request on the server and settles loading immediately', async () => {
      const service = createService({ platform: 'server' });
      expect(service.loading()).toBeFalse();

      await service.restoreSession();

      expect(authToken.ensureCsrfToken).not.toHaveBeenCalled();
      expect(service.loading()).toBeFalse();
    });

    it('clears state without a refresh when the auth feature is off', async () => {
      const service = createService({ flags: { auth: false } });

      await service.restoreSession();

      expect(authToken.ensureCsrfToken).not.toHaveBeenCalled();
      expect(dispatch).toHaveBeenCalledWith(clearUser());
      expect(dispatch).toHaveBeenCalledWith(clearSession());
      expect(service.loading()).toBeFalse();
    });

    it('bootstraps the session and the current user on success', async () => {
      const service = createService();
      const restore = service.restoreSession();
      await flushPromises();

      const request = httpMock.expectOne(`${backendUrl}/auth/refresh`);
      expect(request.request.method).toBe('POST');
      expect(request.request.withCredentials).toBeTrue();
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-123');
      request.flush(envelope(validSession));

      await restore;

      expect(authToken.ensureCsrfToken).toHaveBeenCalledTimes(1);
      expect(dispatch).toHaveBeenCalledWith(setSession({ session: validSession }));
      expect(dispatch).toHaveBeenCalledWith(setUser({ user: makeCurrentUser() }));
      expect(service.loading()).toBeFalse();
    });

    it('omits the CSRF header when no token could be obtained', async () => {
      const service = createService();
      authToken.csrfToken = null;

      const restore = service.restoreSession();
      await flushPromises();

      const request = httpMock.expectOne(`${backendUrl}/auth/refresh`);
      expect(request.request.headers.has('X-CSRF-TOKEN')).toBeFalse();
      request.flush(envelope(validSession));

      await restore;
    });

    it('clears state when the refresh returns no access token', async () => {
      const service = createService();
      const restore = service.restoreSession();
      await flushPromises();

      httpMock.expectOne(`${backendUrl}/auth/refresh`).flush(envelope({ ExpiresAtUtc: 'x' }));
      await restore;

      expect(dispatch).toHaveBeenCalledWith(clearSession());
      expect(authService.me).not.toHaveBeenCalled();
      expect(service.loading()).toBeFalse();
    });

    it('swallows a rejected refresh and still settles loading', async () => {
      const service = createService();
      spyOn(console, 'warn');

      const restore = service.restoreSession();
      await flushPromises();
      httpMock
        .expectOne(`${backendUrl}/auth/refresh`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await restore;

      expect(console.warn).toHaveBeenCalled();
      expect(dispatch).toHaveBeenCalledWith(clearSession());
      expect(service.loading()).toBeFalse();
    });

    it('settles loading even when fetching the current user fails', async () => {
      const service = createService();
      authService.me.and.returnValue(throwError(() => new Error('me failed')));
      spyOn(console, 'warn');

      const restore = service.restoreSession();
      await flushPromises();
      httpMock.expectOne(`${backendUrl}/auth/refresh`).flush(envelope(validSession));
      await restore;

      expect(dispatch).toHaveBeenCalledWith(clearUser());
      expect(service.loading()).toBeFalse();
    });
  });

  describe('bootstrapSession', () => {
    it('rejects a payload without an access token', async () => {
      const service = createService();

      await expectAsync(service.bootstrapSession({ ExpiresAtUtc: 'x' })).toBeRejectedWithError(
        'Authentication response did not include an access token.',
      );
      expect(dispatch).not.toHaveBeenCalled();
    });

    it('accepts a camelCase payload', async () => {
      const service = createService();

      await service.bootstrapSession({
        accessToken: 'token',
        expiresAtUtc: '2026-09-01T00:00:00Z',
      });

      expect(dispatch).toHaveBeenCalledWith(setSession({ session: validSession }));
      expect(dispatch).toHaveBeenCalledWith(setUser({ user: makeCurrentUser() }));
    });

    it('clears the session and rethrows when the user lookup fails', async () => {
      const service = createService();
      const failure = new Error('me failed');
      authService.me.and.returnValue(throwError(() => failure));

      await expectAsync(service.bootstrapSession(validSession)).toBeRejectedWith(failure);

      expect(dispatch).toHaveBeenCalledWith(clearUser());
      expect(dispatch).toHaveBeenCalledWith(clearSession());
    });
  });
});
