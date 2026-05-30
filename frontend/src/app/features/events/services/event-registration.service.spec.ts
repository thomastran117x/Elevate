import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EventRegistrationService, MyRegistrationStatus } from './event-registration.service';

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
      .register(42, { notes: 'front row please', phoneNumber: '416-555-0001', dietaryNeeds: 'Vegan' })
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
    service
      .updateRegistration(42, { notes: 'updated', phoneNumber: '647-555-9999' })
      .subscribe();

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
      data: { isRegistered: true, notes: 'front row', phoneNumber: '416-555-0000', dietaryNeeds: 'Vegan' },
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
});
