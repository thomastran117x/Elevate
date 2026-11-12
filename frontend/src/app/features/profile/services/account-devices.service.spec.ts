import { HttpTestingController } from '@angular/common/http/testing';
import { fakeAsync, tick } from '@angular/core/testing';
import { Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import {
  AccountDevicesService,
  AccountSession,
  SessionRevocation,
  TrustedDevice,
  TrustedDeviceRevocation,
  normalizeSessionRevocation,
  normalizeSessions,
  normalizeTrustedDeviceRevocation,
  normalizeTrustedDevices,
} from './account-devices.service';
import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../core/api/models/api-client-error.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';

describe('AccountDevicesService', () => {
  const sessionsUrl = `${environment.backendUrl}/auth/sessions`;
  const devicesUrl = `${environment.backendUrl}/auth/devices`;
  const session: AccountSession = {
    id: 'a'.repeat(32),
    current: true,
    transport: 'browser',
    client: 'chrome',
    deviceClass: 'desktop',
    network: '203.0.113.0/24',
    rememberMe: true,
    createdAt: '2026-11-01T09:00:00Z',
    lastSeenAt: '2026-11-11T09:00:00Z',
    expiresAt: '2026-12-11T09:00:00Z',
  };
  const device: TrustedDevice = {
    id: 4,
    current: false,
    client: 'firefox',
    deviceClass: 'mobile',
    network: null,
    createdAt: '2026-10-01T09:00:00Z',
    lastSeenAt: '2026-11-10T09:00:00Z',
    expiresAt: '2026-12-30T09:00:00Z',
  };

  let service: AccountDevicesService;
  let httpMock: HttpTestingController;
  let ensureCsrfToken: jasmine.Spy<() => Promise<void>>;

  beforeEach(() => {
    ensureCsrfToken = jasmine.createSpy('ensureCsrfToken').and.resolveTo();

    ({ service, httpMock } = setupService(AccountDevicesService, [
      ApiClient,
      { provide: AuthTokenService, useValue: { ensureCsrfToken } },
    ]));
  });

  afterEach(() => httpMock.verify());

  it('lists sessions with credentials after ensuring a CSRF token', fakeAsync(() => {
    let sessions: AccountSession[] | undefined;
    service.listSessions().subscribe((value) => (sessions = value));
    tick();

    const request = httpMock.expectOne(sessionsUrl);
    expect(ensureCsrfToken).toHaveBeenCalled();
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope({ sessions: [session] }));

    expect(sessions).toEqual([session]);
  }));

  it('lists trusted devices', fakeAsync(() => {
    let devices: TrustedDevice[] | undefined;
    service.listTrustedDevices().subscribe((value) => (devices = value));
    tick();

    const request = httpMock.expectOne(devicesUrl);
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ devices: [device] }));

    expect(devices).toEqual([device]);
  }));

  it('reads PascalCase payloads', fakeAsync(() => {
    let sessions: AccountSession[] | undefined;
    service.listSessions().subscribe((value) => (sessions = value));
    tick();

    httpMock.expectOne(sessionsUrl).flush(
      pascalEnvelope({
        Sessions: [
          {
            Id: session.id,
            Current: true,
            Transport: 'api',
            Client: 'api_client',
            DeviceClass: 'unknown',
            Network: null,
            RememberMe: false,
            CreatedAt: session.createdAt,
            LastSeenAt: session.lastSeenAt,
            ExpiresAt: session.expiresAt,
          },
        ],
      }),
    );

    expect(sessions?.[0]).toEqual(
      jasmine.objectContaining({ id: session.id, transport: 'api', client: 'api_client' }),
    );
  }));

  const revocations: [string, () => Observable<SessionRevocation>, string][] = [
    ['one session', () => service.revokeSession(session.id), `${sessionsUrl}/${session.id}`],
    ['every other session', () => service.revokeOtherSessions(), `${sessionsUrl}/others`],
    ['every session', () => service.revokeAllSessions(), sessionsUrl],
  ];

  revocations.forEach(([what, call, url]) => {
    it(`revokes ${what} with a DELETE and a CSRF token`, fakeAsync(() => {
      let result: SessionRevocation | undefined;
      call().subscribe((value) => (result = value));
      tick();

      const request = httpMock.expectOne(url);
      expect(ensureCsrfToken).toHaveBeenCalled();
      expect(request.request.method).toBe('DELETE');
      expect(request.request.withCredentials).toBeTrue();
      request.flush(envelope({ revokedCount: 2, currentSessionRevoked: false }));

      expect(result).toEqual({ revokedCount: 2, currentSessionRevoked: false });
    }));
  });

  it('forgets one trusted device or all of them', fakeAsync(() => {
    let one: TrustedDeviceRevocation | undefined;
    let all: TrustedDeviceRevocation | undefined;
    service.revokeTrustedDevice(4).subscribe((value) => (one = value));
    service.revokeAllTrustedDevices().subscribe((value) => (all = value));
    tick();

    const single = httpMock.expectOne(`${devicesUrl}/4`);
    expect(single.request.method).toBe('DELETE');
    single.flush(envelope({ revokedCount: 1, currentDeviceRevoked: true }));
    const bulk = httpMock.expectOne(devicesUrl);
    expect(bulk.request.method).toBe('DELETE');
    bulk.flush(envelope({ revokedCount: 3, currentDeviceRevoked: false }));

    expect(one).toEqual({ revokedCount: 1, currentDeviceRevoked: true });
    expect(all).toEqual({ revokedCount: 3, currentDeviceRevoked: false });
  }));

  it('escapes the session id it puts in the path', fakeAsync(() => {
    service.revokeSession('../all').subscribe();
    tick();

    httpMock
      .expectOne(`${sessionsUrl}/..%2Fall`)
      .flush(envelope({ revokedCount: 1, currentSessionRevoked: false }));
  }));

  it('passes a missing step-up proof through as MFA_REQUIRED', fakeAsync(() => {
    let error: unknown;
    service.revokeOtherSessions().subscribe({ error: (err) => (error = err) });
    tick();

    httpMock
      .expectOne(`${sessionsUrl}/others`)
      .flush(errorEnvelope(MFA_REQUIRED_ERROR_CODE, 'Verify again'), {
        status: 403,
        statusText: 'Forbidden',
      });

    expect(error).toEqual(jasmine.any(ApiClientClientError));
    expect((error as ApiClientClientError).code).toBe(MFA_REQUIRED_ERROR_CODE);
  }));

  it('fails a revocation whose result is missing', fakeAsync(() => {
    let error: unknown;
    service.revokeAllTrustedDevices().subscribe({ error: (err) => (error = err) });
    tick();

    httpMock.expectOne(devicesUrl).flush(envelope({}));

    expect(error).toEqual(jasmine.any(Error));
  }));

  describe('normalizers', () => {
    it('drop rows missing what a row needs instead of failing the list', () => {
      expect(
        normalizeSessions({ sessions: [session, { id: 'x' }, null, 'bad'] }).map((s) => s.id),
      ).toEqual([session.id]);
      expect(
        normalizeTrustedDevices({ devices: [device, { id: 5, createdAt: 'x' }] }).map((d) => d.id),
      ).toEqual([4]);
    });

    it('fall back to unknown classes and an empty list', () => {
      const bare: Partial<AccountSession> = { ...session };
      delete bare.client;
      delete bare.deviceClass;
      delete bare.transport;
      expect(normalizeSessions({ sessions: [bare] })[0]).toEqual(
        jasmine.objectContaining({
          client: 'unknown',
          deviceClass: 'unknown',
          transport: 'browser',
        }),
      );
      expect(normalizeSessions(null)).toEqual([]);
      expect(normalizeTrustedDevices({ devices: 'not-a-list' })).toEqual([]);
    });

    it('read revocation results in either casing', () => {
      expect(normalizeSessionRevocation({ CurrentSessionRevoked: true })).toEqual({
        revokedCount: 0,
        currentSessionRevoked: true,
      });
      expect(normalizeSessionRevocation({ revokedCount: 1 })).toBeNull();
      expect(
        normalizeTrustedDeviceRevocation({ RevokedCount: 2, CurrentDeviceRevoked: false }),
      ).toEqual({ revokedCount: 2, currentDeviceRevoked: false });
      expect(normalizeTrustedDeviceRevocation('nope')).toBeNull();
    });
  });
});
