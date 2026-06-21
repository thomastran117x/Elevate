import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService } from './auth.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  ApiClientClientError,
  ApiClientServerError,
  NETWORK_API_ERROR_MESSAGE,
} from '../../../core/api/models/api-client-error.model';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let authToken: jasmine.SpyObj<AuthTokenService>;

  beforeEach(() => {
    authToken = jasmine.createSpyObj<AuthTokenService>('AuthTokenService', ['ensureCsrfToken'], {
      accessToken: null,
      csrfToken: 'csrf-token',
    });
    authToken.ensureCsrfToken.and.resolveTo();

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthTokenService, useValue: authToken },
      ],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('posts login credentials after ensuring csrf', fakeAsync(() => {
    let responseBody: unknown;

    service
      .login({
        email: 'user@example.com',
        password: 'secret123',
        rememberMe: true,
        captcha: 'captcha-token',
      })
      .subscribe((response) => {
        responseBody = response;
      });

    tick();

    const request = httpMock.expectOne((req) => req.url.endsWith('/auth/login'));
    expect(authToken.ensureCsrfToken).toHaveBeenCalled();
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBeTrue();
    expect(request.request.body).toEqual({
      email: 'user@example.com',
      password: 'secret123',
      rememberMe: true,
      captcha: 'captcha-token',
      transport: 'browser',
    });

    request.flush({
      success: true,
      message: 'ok',
      data: {
        AccessToken: 'token',
        ExpiresAtUtc: '2026-06-20T18:00:00Z',
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
      AccessToken: 'token',
      ExpiresAtUtc: '2026-06-20T18:00:00Z',
    });
  }));

  it('surfaces 4xx login failures as typed client errors', fakeAsync(() => {
    let thrown: unknown;

    service
      .login({
        email: 'user@example.com',
        password: 'secret123',
        rememberMe: false,
        captcha: 'captcha-token',
      })
      .subscribe({
        error: (error) => {
          thrown = error;
        },
      });

    tick();

    const request = httpMock.expectOne((req) => req.url.endsWith('/auth/login'));
    request.flush(
      {
        success: false,
        message: 'Validation failed.',
        error: {
          code: 'VALIDATION_ERROR',
          details: { email: ['is required'] },
        },
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe('Validation failed.');
    expect((thrown as ApiClientClientError).code).toBe('VALIDATION_ERROR');
  }));

  it('propagates normalized csrf bootstrap failures before the request is sent', fakeAsync(() => {
    let thrown: unknown;
    authToken.ensureCsrfToken.and.rejectWith(
      new ApiClientServerError(NETWORK_API_ERROR_MESSAGE, 0),
    );

    service
      .login({
        email: 'user@example.com',
        password: 'secret123',
        rememberMe: false,
        captcha: 'captcha-token',
      })
      .subscribe({
        error: (error) => {
          thrown = error;
        },
      });

    tick();

    httpMock.expectNone((req) => req.url.endsWith('/auth/login'));
    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
    expect((thrown as ApiClientServerError).message).toBe(NETWORK_API_ERROR_MESSAGE);
    expect((thrown as ApiClientServerError).status).toBe(0);
  }));
});
