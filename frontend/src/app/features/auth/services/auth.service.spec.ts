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
        returnUrl: '/dashboard',
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
      returnUrl: '/dashboard',
      transport: 'browser',
    });

    request.flush({
      success: true,
      message: 'ok',
      data: {
        Type: 'authenticated',
        Auth: {
          AccessToken: 'token',
          ExpiresAtUtc: '2026-06-20T18:00:00Z',
        },
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
      Type: 'authenticated',
      Auth: {
        AccessToken: 'token',
        ExpiresAtUtc: '2026-06-20T18:00:00Z',
      },
    });
  }));

  it('posts step-up start and verify payloads', fakeAsync(() => {
    let startResponse: unknown;
    let verifyResponse: unknown;

    service.startLoginStepUp('challenge-1', 'sms').subscribe((response) => {
      startResponse = response;
    });
    tick();

    const startRequest = httpMock.expectOne((req) => req.url.endsWith('/auth/mfa/start'));
    expect(startRequest.request.method).toBe('POST');
    expect(startRequest.request.body).toEqual({ challenge: 'challenge-1', method: 'sms' });
    startRequest.flush({
      success: true,
      message: 'ok',
      data: {
        Challenge: 'challenge-2',
        ExpiresAtUtc: '2026-06-22T15:30:00Z',
        SelectedMethod: 'sms',
        MaskedDestination: '***-***-0123',
        CooldownEndsAtUtc: '2026-06-22T15:16:00Z',
        AvailableMethods: ['sms', 'email'],
        MaskedPhone: '***-***-0123',
        MaskedEmail: 'u***@example.com',
      },
      error: null,
      meta: null,
    });

    service.verifyLoginStepUp('challenge-2', '654321').subscribe((response) => {
      verifyResponse = response;
    });
    tick();

    const verifyRequest = httpMock.expectOne((req) => req.url.endsWith('/auth/mfa/verify'));
    expect(verifyRequest.request.method).toBe('POST');
    expect(verifyRequest.request.body).toEqual({ challenge: 'challenge-2', code: '654321' });
    verifyRequest.flush({
      success: true,
      message: 'ok',
      data: {
        AccessToken: 'token',
        ExpiresAtUtc: '2026-06-22T15:31:00Z',
        ReturnPath: '/bookings/123',
      },
      error: null,
      meta: null,
    });

    expect(startResponse).toEqual({
      Challenge: 'challenge-2',
      ExpiresAtUtc: '2026-06-22T15:30:00Z',
      SelectedMethod: 'sms',
      MaskedDestination: '***-***-0123',
      CooldownEndsAtUtc: '2026-06-22T15:16:00Z',
      AvailableMethods: ['sms', 'email'],
      MaskedPhone: '***-***-0123',
      MaskedEmail: 'u***@example.com',
    });
    expect(verifyResponse).toEqual({
      AccessToken: 'token',
      ExpiresAtUtc: '2026-06-22T15:31:00Z',
      ReturnPath: '/bookings/123',
    });
  }));

  it('fetches MFA status after ensuring csrf', fakeAsync(() => {
    let responseBody: unknown;

    service.getMfaStatus().subscribe((response) => {
      responseBody = response;
    });

    tick();

    const request = httpMock.expectOne((req) => req.url.endsWith('/auth/mfa'));
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();

    request.flush({
      success: true,
      message: 'ok',
      data: {
        EnrollmentAvailable: true,
        IsSmsMfaEnabled: false,
        MaskedPhoneNumber: null,
        PhoneVerifiedAtUtc: null,
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
      EnrollmentAvailable: true,
      IsSmsMfaEnabled: false,
      MaskedPhoneNumber: null,
      PhoneVerifiedAtUtc: null,
    });
  }));

  it('posts MFA enrollment start payload with the phone number', fakeAsync(() => {
    let responseBody: unknown;

    service.startMfaEnrollment('+14165550123').subscribe((response) => {
      responseBody = response;
    });

    tick();

    const request = httpMock.expectOne((req) => req.url.endsWith('/auth/mfa/enroll/start'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ phoneNumber: '+14165550123' });

    request.flush({
      success: true,
      message: 'ok',
      data: {
        Challenge: 'challenge-1',
        ExpiresAtUtc: '2026-06-22T15:30:00Z',
        Channel: 'sms',
        MaskedDestination: '***-***-0123',
      },
      error: null,
      meta: null,
    });

    expect(responseBody).toEqual({
      Challenge: 'challenge-1',
      ExpiresAtUtc: '2026-06-22T15:30:00Z',
      Channel: 'sms',
      MaskedDestination: '***-***-0123',
    });
  }));

  it('posts MFA verification and disable payloads', fakeAsync(() => {
    let verified: unknown;
    let disabled: unknown;

    service.verifyMfaEnrollment('654321', 'challenge-1').subscribe((response) => {
      verified = response;
    });
    tick();

    const verifyRequest = httpMock.expectOne((req) => req.url.endsWith('/auth/mfa/enroll/verify'));
    expect(verifyRequest.request.method).toBe('POST');
    expect(verifyRequest.request.body).toEqual({ code: '654321', challenge: 'challenge-1' });
    verifyRequest.flush({
      success: true,
      message: 'ok',
      data: {
        EnrollmentAvailable: true,
        IsSmsMfaEnabled: true,
        MaskedPhoneNumber: '***-***-0123',
        PhoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
      },
      error: null,
      meta: null,
    });

    service.disableMfa().subscribe((response) => {
      disabled = response;
    });
    tick();

    const disableRequest = httpMock.expectOne((req) => req.url.endsWith('/auth/mfa/disable'));
    expect(disableRequest.request.method).toBe('POST');
    expect(disableRequest.request.body).toEqual({});
    disableRequest.flush({
      success: true,
      message: 'ok',
      data: {
        EnrollmentAvailable: true,
        IsSmsMfaEnabled: false,
        MaskedPhoneNumber: '***-***-0123',
        PhoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
      },
      error: null,
      meta: null,
    });

    expect(verified).toEqual({
      EnrollmentAvailable: true,
      IsSmsMfaEnabled: true,
      MaskedPhoneNumber: '***-***-0123',
      PhoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
    });
    expect(disabled).toEqual({
      EnrollmentAvailable: true,
      IsSmsMfaEnabled: false,
      MaskedPhoneNumber: '***-***-0123',
      PhoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
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
