import {
  HTTP_INTERCEPTORS,
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptorsFromDi,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';

import { RefreshTokenInterceptor } from './refresh.interceptor';
import { AuthTokenService } from '../services/auth-token.service';

describe('RefreshTokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: {
    accessToken: string | null;
    refreshAccessToken: jasmine.Spy<() => Promise<void>>;
    logoutLocal: jasmine.Spy<() => void>;
  };

  beforeEach(() => {
    auth = {
      accessToken: 'stale-token',
      refreshAccessToken: jasmine.createSpy('refreshAccessToken').and.callFake(async () => {
        auth.accessToken = 'fresh-token';
      }),
      logoutLocal: jasmine.createSpy('logoutLocal'),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: AuthTokenService, useValue: auth },
        { provide: HTTP_INTERCEPTORS, useClass: RefreshTokenInterceptor, multi: true },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function unauthorized(url: string) {
    const request = httpMock.expectOne(url);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    return request;
  }

  it('refreshes once and replays the request with the new token', fakeAsync(() => {
    let body: unknown;
    http.get('/api/clubs').subscribe((response) => (body = response));

    unauthorized('/api/clubs');
    tick();

    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1);

    const retry = httpMock.expectOne('/api/clubs');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer fresh-token');
    expect(retry.request.withCredentials).toBeTrue();
    retry.flush({ ok: true });
    tick();

    expect(body).toEqual({ ok: true });
  }));

  it('shares a single refresh between concurrent 401s and replays both requests', fakeAsync(() => {
    const results: string[] = [];
    http.get('/api/clubs').subscribe(() => results.push('clubs'));
    http.get('/api/events').subscribe(() => results.push('events'));

    unauthorized('/api/clubs');
    unauthorized('/api/events');
    tick();

    // The old boolean guard let the first 401 win and dropped the second on the
    // floor; both must now ride the same in-flight refresh.
    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1);

    httpMock.expectOne('/api/clubs').flush({});
    httpMock.expectOne('/api/events').flush({});
    tick();

    expect(results).toEqual(['clubs', 'events']);
  }));

  it('refreshes again for a 401 that arrives after the previous refresh settled', fakeAsync(() => {
    http.get('/api/clubs').subscribe();
    unauthorized('/api/clubs');
    tick();
    httpMock.expectOne('/api/clubs').flush({});
    tick();

    http.get('/api/events').subscribe();
    unauthorized('/api/events');
    tick();
    httpMock.expectOne('/api/events').flush({});
    tick();

    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(2);
  }));

  it('gives up instead of looping when the replayed request also 401s', fakeAsync(() => {
    let error: HttpErrorResponse | undefined;
    http.get('/api/clubs').subscribe({ error: (err) => (error = err) });

    unauthorized('/api/clubs');
    tick();

    unauthorized('/api/clubs');
    tick();

    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(error?.status).toBe(401);
    expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
  }));

  it('clears the local session and rethrows when the refresh itself fails', fakeAsync(() => {
    auth.refreshAccessToken.and.returnValue(Promise.reject(new Error('refresh rejected')));

    let error: unknown;
    http.get('/api/clubs').subscribe({ error: (err) => (error = err) });

    unauthorized('/api/clubs');
    tick();

    expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
    expect((error as Error).message).toBe('refresh rejected');
  }));

  it('never tries to refresh an auth endpoint', fakeAsync(() => {
    let error: HttpErrorResponse | undefined;
    http.post('/api/auth/login', {}).subscribe({ error: (err) => (error = err) });

    unauthorized('/api/auth/login');
    tick();

    expect(auth.refreshAccessToken).not.toHaveBeenCalled();
    expect(error?.status).toBe(401);
  }));

  it('passes non-401 failures straight through', fakeAsync(() => {
    let error: HttpErrorResponse | undefined;
    http.get('/api/clubs').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/clubs').flush(null, { status: 500, statusText: 'Server Error' });
    tick();

    expect(auth.refreshAccessToken).not.toHaveBeenCalled();
    expect(error?.status).toBe(500);
  }));

  it('replays without an Authorization header when the refresh yields no token', fakeAsync(() => {
    auth.refreshAccessToken.and.callFake(async () => {
      auth.accessToken = null;
    });

    http.get('/api/clubs').subscribe();
    unauthorized('/api/clubs');
    tick();

    const retry = httpMock.expectOne('/api/clubs');
    expect(retry.request.headers.has('Authorization')).toBeFalse();
    retry.flush({});
    tick();
  }));
});
