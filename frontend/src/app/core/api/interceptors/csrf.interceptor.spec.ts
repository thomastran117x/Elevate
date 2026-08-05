import {
  HTTP_INTERCEPTORS,
  HttpClient,
  provideHttpClient,
  withInterceptorsFromDi,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '@environments/environment';

import { CsrfInterceptor } from './csrf.interceptor';
import { AuthTokenService } from '../services/auth-token.service';

describe('CsrfInterceptor', () => {
  const backendUrl = environment.backendUrl;
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: { csrfToken: string | null };

  beforeEach(() => {
    auth = { csrfToken: 'csrf-123' };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: AuthTokenService, useValue: auth },
        { provide: HTTP_INTERCEPTORS, useClass: CsrfInterceptor, multi: true },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function expectCsrfHeader(url: string, expected: string | null): void {
    const request = httpMock.expectOne(url);
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe(expected);
    request.flush({});
  }

  for (const method of ['POST', 'PUT', 'PATCH', 'DELETE'] as const) {
    it(`adds the CSRF header to a backend ${method}`, () => {
      http.request(method, `${backendUrl}/clubs`, { body: {} }).subscribe();

      expectCsrfHeader(`${backendUrl}/clubs`, 'csrf-123');
    });
  }

  for (const method of ['GET', 'HEAD', 'OPTIONS'] as const) {
    it(`leaves a safe ${method} unchanged`, () => {
      http.request(method, `${backendUrl}/clubs`).subscribe();

      expectCsrfHeader(`${backendUrl}/clubs`, null);
    });
  }

  it('accepts a lowercase method name', () => {
    http.request('post', `${backendUrl}/clubs`, { body: {} }).subscribe();

    expectCsrfHeader(`${backendUrl}/clubs`, 'csrf-123');
  });

  it('does not leak the token to a third-party host', () => {
    http.post('https://third-party.example.com/collect', {}).subscribe();

    expectCsrfHeader('https://third-party.example.com/collect', null);
  });

  it('skips the header when no token has been fetched yet', () => {
    auth.csrfToken = null;

    http.post(`${backendUrl}/clubs`, {}).subscribe();

    expectCsrfHeader(`${backendUrl}/clubs`, null);
  });
});
