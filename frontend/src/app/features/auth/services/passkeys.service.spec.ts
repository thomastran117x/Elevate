import { HttpTestingController } from '@angular/common/http/testing';
import { fakeAsync, tick } from '@angular/core/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, setupService } from '@testing';

import {
  Passkey,
  PasskeyList,
  PasskeyOutcome,
  PasskeysService,
  normalizePasskey,
  normalizePasskeyCeremony,
  normalizePasskeyList,
} from './passkeys.service';
import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../core/api/models/api-client-error.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { AuthenticatedSessionResponse } from '../../../core/models/auth-response.model';
import { WebAuthnResult, WebAuthnService } from '../../../core/webauthn/webauthn.service';

describe('PasskeysService', () => {
  const baseUrl = `${environment.backendUrl}/auth/passkeys`;
  const passkey: Passkey = {
    id: 3,
    name: 'Work laptop',
    syncable: true,
    backedUp: true,
    canRemove: true,
    createdAt: '2026-11-01T09:00:00Z',
    lastUsedAt: null,
  };
  const ceremony = { ceremonyId: 'ceremony-id', publicKey: { challenge: 'abc' } };
  const credential = { id: 'cred', type: 'public-key' };

  let service: PasskeysService;
  let httpMock: HttpTestingController;
  let ensureCsrfToken: jasmine.Spy<() => Promise<void>>;
  let webAuthn: jasmine.SpyObj<WebAuthnService>;

  beforeEach(() => {
    ensureCsrfToken = jasmine.createSpy('ensureCsrfToken').and.resolveTo();
    webAuthn = jasmine.createSpyObj<WebAuthnService>('WebAuthnService', [
      'isSupported',
      'isConditionalMediationAvailable',
      'create',
      'get',
    ]);

    ({ service, httpMock } = setupService(PasskeysService, [
      ApiClient,
      { provide: AuthTokenService, useValue: { ensureCsrfToken } },
      { provide: WebAuthnService, useValue: webAuthn },
    ]));
  });

  afterEach(() => httpMock.verify());

  it('reports browser support from the WebAuthn service', async () => {
    webAuthn.isSupported.and.returnValue(true);
    webAuthn.isConditionalMediationAvailable.and.resolveTo(true);

    expect(service.isSupported()).toBeTrue();
    expect(await service.isConditionalMediationAvailable()).toBeTrue();
  });

  it('lists passkeys with credentials after ensuring a CSRF token', fakeAsync(() => {
    let list: PasskeyList | undefined;
    service.list().subscribe((value) => (list = value));
    tick();

    const request = httpMock.expectOne(baseUrl);
    expect(ensureCsrfToken).toHaveBeenCalled();
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope({ passkeys: [passkey], limit: 10 }));

    expect(list).toEqual({ passkeys: [passkey], limit: 10 });
  }));

  it('registers: options, then the browser prompt, then the result', fakeAsync(() => {
    webAuthn.create.and.resolveTo({ ok: true, credential });
    let outcome: PasskeyOutcome<Passkey> | undefined;
    service.register('Work laptop').subscribe((value) => (outcome = value));
    tick();

    const options = httpMock.expectOne(`${baseUrl}/registration/options`);
    expect(options.request.method).toBe('POST');
    expect(options.request.body).toEqual({ name: 'Work laptop' });
    options.flush(envelope(ceremony));
    tick();

    expect(webAuthn.create).toHaveBeenCalledWith(ceremony.publicKey);
    const completion = httpMock.expectOne(`${baseUrl}/registration`);
    expect(completion.request.body).toEqual({ ceremonyId: 'ceremony-id', credential });
    completion.flush(envelope(passkey));

    expect(outcome).toEqual({ status: 'done', value: passkey });
  }));

  it('sends no name when none was given', fakeAsync(() => {
    webAuthn.create.and.resolveTo({ ok: false, reason: 'cancelled' });
    service.register('').subscribe();
    tick();

    const options = httpMock.expectOne(`${baseUrl}/registration/options`);
    expect(options.request.body).toEqual({ name: null });
    options.flush(envelope(ceremony));
    tick();
  }));

  const failures: WebAuthnResult[] = [
    { ok: false, reason: 'cancelled' },
    { ok: false, reason: 'already_registered' },
    { ok: false, reason: 'unsupported' },
  ];
  for (const failure of failures) {
    it(`reports a ${!failure.ok && failure.reason} prompt without completing the ceremony`, fakeAsync(() => {
      webAuthn.create.and.resolveTo(failure);
      let outcome: PasskeyOutcome<Passkey> | undefined;
      service.register(null).subscribe((value) => (outcome = value));
      tick();

      httpMock.expectOne(`${baseUrl}/registration/options`).flush(envelope(ceremony));
      tick();

      expect(outcome?.status).toBe(!failure.ok ? failure.reason : 'done');
      httpMock.expectNone(`${baseUrl}/registration`);
    }));
  }

  it('surfaces a refused registration as an error with its code', fakeAsync(() => {
    let error: unknown;
    service.register(null).subscribe({ error: (err) => (error = err) });
    tick();

    httpMock
      .expectOne(`${baseUrl}/registration/options`)
      .flush(errorEnvelope(MFA_REQUIRED_ERROR_CODE, 'Verify first'), {
        status: 403,
        statusText: 'Forbidden',
      });

    expect(error).toEqual(jasmine.any(ApiClientClientError));
    expect((error as ApiClientClientError).code).toBe(MFA_REQUIRED_ERROR_CODE);
    expect(webAuthn.create).not.toHaveBeenCalled();
  }));

  it('fails clearly when the options are incomplete', fakeAsync(() => {
    let error: unknown;
    service.register(null).subscribe({ error: (err) => (error = err) });
    tick();

    httpMock.expectOne(`${baseUrl}/registration/options`).flush(envelope({ ceremonyId: 'x' }));

    expect((error as Error).message).toContain('Passkey options');
  }));

  it('renames with a PATCH and removes with a DELETE', fakeAsync(() => {
    let renamed: Passkey | undefined;
    let removed = false;
    service.rename(3, 'Phone').subscribe((value) => (renamed = value));
    service.remove(3).subscribe(() => (removed = true));
    tick();

    const rename = httpMock.expectOne(
      (request) => request.method === 'PATCH' && request.url === `${baseUrl}/3`,
    );
    expect(rename.request.body).toEqual({ name: 'Phone' });
    rename.flush(envelope({ ...passkey, name: 'Phone' }));
    httpMock
      .expectOne((request) => request.method === 'DELETE' && request.url === `${baseUrl}/3`)
      .flush(envelope(null));

    expect(renamed?.name).toBe('Phone');
    expect(removed).toBeTrue();
  }));

  it('signs in through the browser transport and returns the session', fakeAsync(() => {
    webAuthn.get.and.resolveTo({ ok: true, credential });
    const controller = new AbortController();
    let outcome: PasskeyOutcome<AuthenticatedSessionResponse> | undefined;
    service
      .signIn(true, { mediation: 'conditional', signal: controller.signal })
      .subscribe((value) => (outcome = value));
    tick();

    httpMock.expectOne(`${baseUrl}/sign-in/options`).flush(envelope(ceremony));
    tick();

    expect(webAuthn.get).toHaveBeenCalledWith(ceremony.publicKey, {
      mediation: 'conditional',
      signal: controller.signal,
    });
    const completion = httpMock.expectOne(`${baseUrl}/sign-in`);
    expect(completion.request.body).toEqual({
      ceremonyId: 'ceremony-id',
      credential,
      rememberMe: true,
      transport: 'browser',
    });
    completion.flush(
      envelope({
        type: 'authenticated',
        auth: { accessToken: 'access', expiresAtUtc: '2099-01-01T00:00:00Z' },
      }),
    );

    expect(outcome?.status).toBe('done');
    expect(outcome?.value?.AccessToken).toBe('access');
  }));

  it('refuses a sign-in response without a session', fakeAsync(() => {
    webAuthn.get.and.resolveTo({ ok: true, credential });
    let error: unknown;
    service.signIn(false).subscribe({ error: (err) => (error = err) });
    tick();

    httpMock.expectOne(`${baseUrl}/sign-in/options`).flush(envelope(ceremony));
    tick();
    httpMock.expectOne(`${baseUrl}/sign-in`).flush(envelope({ type: 'authenticated' }));

    expect((error as Error).message).toContain('Passkey sign-in');
  }));

  it('reports a dismissed sign-in prompt', fakeAsync(() => {
    webAuthn.get.and.resolveTo({ ok: false, reason: 'cancelled' });
    let outcome: PasskeyOutcome<AuthenticatedSessionResponse> | undefined;
    service.signIn(false).subscribe((value) => (outcome = value));
    tick();

    httpMock.expectOne(`${baseUrl}/sign-in/options`).flush(envelope(ceremony));
    tick();

    expect(outcome).toEqual({ status: 'cancelled' });
    httpMock.expectNone(`${baseUrl}/sign-in`);
  }));

  it('steps up with a passkey, whose answer carries no data', fakeAsync(() => {
    webAuthn.get.and.resolveTo({ ok: true, credential });
    let outcome: PasskeyOutcome<void> | undefined;
    service.stepUp().subscribe((value) => (outcome = value));
    tick();

    httpMock.expectOne(`${baseUrl}/step-up/options`).flush(envelope(ceremony));
    tick();
    const completion = httpMock.expectOne(`${baseUrl}/step-up`);
    expect(completion.request.body).toEqual({ ceremonyId: 'ceremony-id', credential });
    completion.flush(envelope(null));

    expect(outcome?.status).toBe('done');
  }));

  it('reports a dismissed step-up prompt', fakeAsync(() => {
    webAuthn.get.and.resolveTo({ ok: false, reason: 'unsupported' });
    let outcome: PasskeyOutcome<void> | undefined;
    service.stepUp().subscribe((value) => (outcome = value));
    tick();

    httpMock.expectOne(`${baseUrl}/step-up/options`).flush(envelope(ceremony));
    tick();

    expect(outcome).toEqual({ status: 'unsupported' });
  }));

  describe('normalizers', () => {
    it('reads PascalCase payloads', () => {
      expect(
        normalizePasskeyList({
          Passkeys: [
            {
              Id: 3,
              Name: 'Work laptop',
              Syncable: true,
              BackedUp: true,
              CanRemove: true,
              CreatedAt: passkey.createdAt,
              LastUsedAt: null,
            },
          ],
          Limit: 10,
        }),
      ).toEqual({ passkeys: [passkey], limit: 10 });
      expect(normalizePasskeyCeremony({ CeremonyId: 'x', PublicKey: { a: 1 } })).toEqual({
        ceremonyId: 'x',
        publicKey: { a: 1 },
      });
    });

    it('drops rows missing what a row needs, and defaults the rest', () => {
      expect(
        normalizePasskeyList({
          passkeys: [{ id: 1 }, null, { id: 2, name: 'Key', createdAt: passkey.createdAt }],
        }),
      ).toEqual({
        passkeys: [
          {
            id: 2,
            name: 'Key',
            syncable: false,
            backedUp: false,
            canRemove: false,
            createdAt: passkey.createdAt,
            lastUsedAt: null,
          },
        ],
        limit: 0,
      });
      expect(normalizePasskeyList(null)).toEqual({ passkeys: [], limit: 0 });
      expect(normalizePasskey('nope')).toBeNull();
      expect(normalizePasskeyCeremony({ ceremonyId: 'x' })).toBeNull();
      expect(normalizePasskeyCeremony(undefined)).toBeNull();
    });
  });
});
