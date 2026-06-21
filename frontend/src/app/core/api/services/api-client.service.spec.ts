import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ApiClient } from './api-client.service';
import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
  NETWORK_API_ERROR_MESSAGE,
  TEMPORARY_SERVER_API_ERROR_MESSAGE,
} from '../models/api-client-error.model';

describe('ApiClient', () => {
  let service: ApiClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ApiClient, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(ApiClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  for (const status of [400, 401, 403, 404, 409, 422]) {
    it(`preserves ${status} responses as client errors`, () => {
      let thrown: unknown;

      service.get('/test').subscribe({
        error: (error) => {
          thrown = error;
        },
      });

      const request = httpMock.expectOne('/test');
      request.flush(
        {
          success: false,
          message: `Request failed with ${status}.`,
          error: {
            code: 'VALIDATION_ERROR',
            details: { field: ['is invalid'] },
          },
        },
        { status, statusText: 'Client Error' },
      );

      expect(thrown).toEqual(jasmine.any(ApiClientClientError));
      expect((thrown as ApiClientClientError).kind).toBe('client');
      expect((thrown as ApiClientClientError).status).toBe(status);
      expect((thrown as ApiClientClientError).message).toBe(`Request failed with ${status}.`);
      expect((thrown as ApiClientClientError).code).toBe('VALIDATION_ERROR');
      expect((thrown as ApiClientClientError).details).toEqual({ field: ['is invalid'] });
    });
  }

  it('uses the proper UX message for a 500 response', () => {
    let thrown: unknown;

    service.get('/test').subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const request = httpMock.expectOne('/test');
    request.flush(
      {
        success: false,
        message: 'Sensitive backend failure.',
        error: { code: 'SERVER_BROKE' },
      },
      { status: 500, statusText: 'Server Error' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
    expect((thrown as ApiClientServerError).kind).toBe('server');
    expect((thrown as ApiClientServerError).status).toBe(500);
    expect((thrown as ApiClientServerError).message).toBe(GENERIC_API_ERROR_MESSAGE);
  });

  for (const status of [502, 503, 504]) {
    it(`uses the temporary outage message for ${status} responses`, () => {
      let thrown: unknown;

      service.get('/test').subscribe({
        error: (error) => {
          thrown = error;
        },
      });

      const request = httpMock.expectOne('/test');
      request.flush(
        {
          success: false,
          message: 'Sensitive backend failure.',
          error: { code: 'SERVER_BROKE' },
        },
        { status, statusText: 'Server Error' },
      );

      expect(thrown).toEqual(jasmine.any(ApiClientServerError));
      expect((thrown as ApiClientServerError).kind).toBe('server');
      expect((thrown as ApiClientServerError).status).toBe(status);
      expect((thrown as ApiClientServerError).message).toBe(TEMPORARY_SERVER_API_ERROR_MESSAGE);
    });
  }

  it('uses the network UX message for connection failures', () => {
    let thrown: unknown;

    service.get('/test').subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const request = httpMock.expectOne('/test');
    request.error(new ProgressEvent('error'));

    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
    expect((thrown as ApiClientServerError).kind).toBe('server');
    expect((thrown as ApiClientServerError).status).toBe(0);
    expect((thrown as ApiClientServerError).message).toBe(NETWORK_API_ERROR_MESSAGE);
  });

  it('preserves PascalCase envelope metadata for client errors', () => {
    let thrown: unknown;

    service.get('/test').subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const request = httpMock.expectOne('/test');
    request.flush(
      {
        success: false,
        Message: 'Validation failed.',
        error: {
          Code: 'VALIDATION_ERROR',
          Details: { email: ['is required'] },
        },
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe('Validation failed.');
    expect((thrown as ApiClientClientError).code).toBe('VALIDATION_ERROR');
    expect((thrown as ApiClientClientError).details).toEqual({ email: ['is required'] });
  });

  it('wraps non-http errors as generic server errors', () => {
    const error = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });

    expect((service as any).normalizeError(error)).toEqual(jasmine.any(ApiClientServerError));
    expect((service as any).normalizeError('plain failure').message).toBe(
      GENERIC_API_ERROR_MESSAGE,
    );
  });
});
