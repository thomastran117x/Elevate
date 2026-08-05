import {
  HTTP_INTERCEPTORS,
  HttpClient,
  provideHttpClient,
  withInterceptorsFromDi,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AccessTokenInterceptor } from './access.interceptor';
import { AuthTokenService } from '../services/auth-token.service';

describe('AccessTokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: { accessToken: string | null };

  beforeEach(() => {
    auth = { accessToken: null };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: AuthTokenService, useValue: auth },
        { provide: HTTP_INTERCEPTORS, useClass: AccessTokenInterceptor, multi: true },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('attaches a Bearer header when an access token is held', () => {
    auth.accessToken = 'token-123';

    http.get('/api/clubs').subscribe();

    const request = httpMock.expectOne('/api/clubs');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('omits the Authorization header when there is no token', () => {
    http.get('/api/clubs').subscribe();

    const request = httpMock.expectOne('/api/clubs');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('sends credentials whether or not a token is held', () => {
    http.get('/api/clubs').subscribe();
    const anonymous = httpMock.expectOne('/api/clubs');
    expect(anonymous.request.withCredentials).toBeTrue();
    anonymous.flush({});

    auth.accessToken = 'token-123';
    http.get('/api/clubs').subscribe();
    const authenticated = httpMock.expectOne('/api/clubs');
    expect(authenticated.request.withCredentials).toBeTrue();
    authenticated.flush({});
  });

  it('leaves the rest of the request untouched', () => {
    auth.accessToken = 'token-123';

    http.post('/api/clubs', { name: 'Robotics' }, { headers: { 'X-Custom': 'kept' } }).subscribe();

    const request = httpMock.expectOne('/api/clubs');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Robotics' });
    expect(request.request.headers.get('X-Custom')).toBe('kept');
    request.flush({});
  });
});
