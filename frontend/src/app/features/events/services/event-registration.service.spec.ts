import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EventRegistrationService, MyRegistrationStatus } from './event-registration.service';
import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
} from '../../../core/api/models/api-client-error.model';

describe('EventRegistrationService', () => {
  let service: EventRegistrationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [EventRegistrationService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(EventRegistrationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('sends a POST to register for an event with an empty body when no details given', () => {
    service.register(42).subscribe();

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/register'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('sends a POST with detail fields when details are provided', () => {
    service
      .register(42, {
        notes: 'front row please',
        phoneNumber: '416-555-0001',
        dietaryNeeds: 'Vegan',
      })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/register'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      notes: 'front row please',
      phoneNumber: '416-555-0001',
      dietaryNeeds: 'Vegan',
    });
    req.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('sends a DELETE to unregister from an event', () => {
    service.unregister(42).subscribe();

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/register'));
    expect(req.request.method).toBe('DELETE');
    req.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('sends a PATCH to update registration details', () => {
    service.updateRegistration(42, { notes: 'updated', phoneNumber: '647-555-9999' }).subscribe();

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/register'));
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ notes: 'updated', phoneNumber: '647-555-9999' });
    req.flush({ success: true, message: 'ok', data: null, error: null, meta: null });
  });

  it('returns isRegistered=true and details when registered (camelCase payload)', () => {
    let result: MyRegistrationStatus | undefined;
    service.checkRegistration(42).subscribe((v) => (result = v));

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/registrations/me'));
    req.flush({
      success: true,
      message: 'ok',
      data: {
        isRegistered: true,
        notes: 'front row',
        phoneNumber: '416-555-0000',
        dietaryNeeds: 'Vegan',
      },
      error: null,
      meta: null,
    });
    expect(result?.isRegistered).toBeTrue();
    expect(result?.details?.notes).toBe('front row');
    expect(result?.details?.phoneNumber).toBe('416-555-0000');
    expect(result?.details?.dietaryNeeds).toBe('Vegan');
  });

  it('returns isRegistered=false with null details when not registered (camelCase)', () => {
    let result: MyRegistrationStatus | undefined;
    service.checkRegistration(42).subscribe((v) => (result = v));

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/registrations/me'));
    req.flush({
      success: true,
      message: 'ok',
      data: { isRegistered: false },
      error: null,
      meta: null,
    });
    expect(result?.isRegistered).toBeFalse();
    expect(result?.details).toBeNull();
  });

  it('returns isRegistered=false with null details when not registered (PascalCase)', () => {
    let result: MyRegistrationStatus | undefined;
    service.checkRegistration(42).subscribe((v) => (result = v));

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/registrations/me'));
    req.flush({
      success: true,
      message: 'ok',
      data: { IsRegistered: false },
      error: null,
      meta: null,
    });
    expect(result?.isRegistered).toBeFalse();
    expect(result?.details).toBeNull();
  });

  it('surfaces 4xx registration failures as typed client errors', () => {
    let thrown: unknown;

    service.register(42).subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/register'));
    req.flush(
      {
        success: false,
        message: 'Validation failed.',
        error: {
          code: 'VALIDATION_ERROR',
          details: { notes: ['is required'] },
        },
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).message).toBe('Validation failed.');
    expect((thrown as ApiClientClientError).code).toBe('VALIDATION_ERROR');
  });

  it('collapses 5xx registration failures to the generic adapter error', () => {
    let thrown: unknown;

    service.unregister(42).subscribe({
      error: (error) => {
        thrown = error;
      },
    });

    const req = httpMock.expectOne((r) => r.url.includes('/events/42/register'));
    req.flush(
      {
        success: false,
        message: 'Sensitive backend failure.',
        error: { code: 'SERVER_FAILURE' },
      },
      { status: 500, statusText: 'Server Error' },
    );

    expect(thrown).toEqual(jasmine.any(ApiClientServerError));
    expect((thrown as ApiClientServerError).message).toBe(GENERIC_API_ERROR_MESSAGE);
  });
});
