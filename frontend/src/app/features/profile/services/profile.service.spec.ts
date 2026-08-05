import { HttpTestingController } from '@angular/common/http/testing';
import { fakeAsync, tick } from '@angular/core/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, setupService } from '@testing';

import { MyProfile, ProfileService } from './profile.service';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { ApiClientClientError } from '../../../core/api/models/api-client-error.model';

describe('ProfileService', () => {
  const base = `${environment.backendUrl}/profile`;
  const profile: MyProfile = {
    Id: 1,
    Email: 'member@example.com',
    Username: 'member',
    Name: 'Test Member',
    Avatar: null,
    Usertype: 'User',
    Phone: null,
    Address: null,
    GoogleLinked: false,
    MicrosoftLinked: false,
    CreatedAtUtc: '2026-01-01T00:00:00Z',
    UpdatedAtUtc: '2026-01-02T00:00:00Z',
  };

  let service: ProfileService;
  let httpMock: HttpTestingController;
  let ensureCsrfToken: jasmine.Spy<() => Promise<void>>;

  beforeEach(() => {
    ensureCsrfToken = jasmine.createSpy('ensureCsrfToken').and.resolveTo();

    ({ service, httpMock } = setupService(ProfileService, [
      ApiClient,
      { provide: AuthTokenService, useValue: { ensureCsrfToken } },
    ]));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('bootstraps CSRF before reading the signed-in profile', fakeAsync(() => {
    let result: MyProfile | undefined;
    service.getMyProfile().subscribe((value) => (result = value));
    tick();

    expect(ensureCsrfToken).toHaveBeenCalledTimes(1);
    const request = httpMock.expectOne(base);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(profile));

    expect(result).toEqual(profile);
  }));

  it('reads a public profile without CSRF and URL-encodes the username', () => {
    let result: unknown;
    service.getPublicProfile('jamie rivers/1').subscribe((value) => (result = value));

    expect(ensureCsrfToken).not.toHaveBeenCalled();
    const request = httpMock.expectOne(`${base}/jamie%20rivers%2F1`);
    expect(request.request.method).toBe('GET');
    request.flush(
      envelope({
        Username: 'jamie',
        Name: 'Jamie',
        Avatar: null,
        Usertype: 'User',
        CreatedAtUtc: '2026-01-01T00:00:00Z',
      }),
    );

    expect(result).toEqual(jasmine.objectContaining({ Username: 'jamie' }));
  });

  it('patches only the supplied profile fields', fakeAsync(() => {
    service.updateProfile({ name: 'New Name', phone: '555-1111' }).subscribe();
    tick();

    const request = httpMock.expectOne(base);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ name: 'New Name', phone: '555-1111' });
    request.flush(envelope(profile));
  }));

  it('uploads the avatar as multipart form data', fakeAsync(() => {
    const file = new File(['bytes'], 'avatar.png', { type: 'image/png' });

    service.uploadAvatar(file).subscribe();
    tick();

    const request = httpMock.expectOne(`${base}/avatar`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBeTrue();
    expect((request.request.body as FormData).get('image')).toBe(file);
    request.flush(envelope(profile));
  }));

  it('posts both passwords to the change-password endpoint', fakeAsync(() => {
    service.changePassword('old-secret', 'new-secret').subscribe();
    tick();

    const request = httpMock.expectOne(`${base}/change-password`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      currentPassword: 'old-secret',
      newPassword: 'new-secret',
    });
    request.flush(null);
  }));

  it('deletes the account after bootstrapping CSRF', fakeAsync(() => {
    service.deleteAccount().subscribe();
    tick();

    expect(ensureCsrfToken).toHaveBeenCalledTimes(1);
    const request = httpMock.expectOne(base);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(null);
  }));

  it('errors when the profile envelope carries no data', fakeAsync(() => {
    let thrown: Error | undefined;
    service.getMyProfile().subscribe({ error: (err: Error) => (thrown = err) });
    tick();

    httpMock.expectOne(base).flush(envelope(null, { message: '' }));

    expect(thrown?.message).toBe('Profile response was incomplete.');
  }));

  it('surfaces a 4xx as a typed client error', fakeAsync(() => {
    let thrown: unknown;
    service.changePassword('wrong', 'new').subscribe({ error: (err) => (thrown = err) });
    tick();

    httpMock
      .expectOne(`${base}/change-password`)
      .flush(errorEnvelope('INVALID_CREDENTIALS', 'Current password is incorrect.'), {
        status: 400,
        statusText: 'Bad Request',
      });

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).code).toBe('INVALID_CREDENTIALS');
  }));
});
