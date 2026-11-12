import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { WebAuthnService } from './webauthn.service';

interface Statics {
  parseCreationOptionsFromJSON?: (options: unknown) => PublicKeyCredentialCreationOptions;
  parseRequestOptionsFromJSON?: (options: unknown) => PublicKeyCredentialRequestOptions;
  isConditionalMediationAvailable?: () => Promise<boolean>;
}

const creationJson = {
  rp: { id: 'localhost', name: 'EventXperience' },
  user: { id: 'AQID', name: 'owner', displayName: 'Owner' },
  challenge: 'AAEC',
  pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
};
const requestJson = { challenge: 'AAEC', rpId: 'localhost', allowCredentials: [] };

function serializableCredential(json: Record<string, unknown>): Credential {
  return { type: 'public-key', id: 'CQk', toJSON: () => json } as unknown as Credential;
}

function domError(name: string): DOMException {
  return new DOMException('refused', name);
}

describe('WebAuthnService', () => {
  const statics = window.PublicKeyCredential as unknown as Statics;
  const saved: Statics = {};

  beforeEach(() => {
    saved.parseCreationOptionsFromJSON = statics.parseCreationOptionsFromJSON;
    saved.parseRequestOptionsFromJSON = statics.parseRequestOptionsFromJSON;
    saved.isConditionalMediationAvailable = statics.isConditionalMediationAvailable;
  });

  afterEach(() => {
    statics.parseCreationOptionsFromJSON = saved.parseCreationOptionsFromJSON;
    statics.parseRequestOptionsFromJSON = saved.parseRequestOptionsFromJSON;
    statics.isConditionalMediationAvailable = saved.isConditionalMediationAvailable;
  });

  function create(platform = 'browser'): WebAuthnService {
    TestBed.configureTestingModule({ providers: [{ provide: PLATFORM_ID, useValue: platform }] });
    return TestBed.inject(WebAuthnService);
  }

  describe('during server rendering', () => {
    it('reports no support and never touches the browser APIs', async () => {
      const service = create('server');
      const createSpy = spyOn(navigator.credentials, 'create');

      expect(service.isSupported()).toBeFalse();
      expect(await service.isConditionalMediationAvailable()).toBeFalse();
      expect(await service.create(creationJson)).toEqual({ ok: false, reason: 'unsupported' });
      expect(await service.get(requestJson)).toEqual({ ok: false, reason: 'unsupported' });
      expect(createSpy).not.toHaveBeenCalled();
    });
  });

  describe('in the browser', () => {
    let service: WebAuthnService;

    beforeEach(() => {
      service = create();
    });

    it('reports support where WebAuthn is available', () => {
      expect(service.isSupported()).toBeTrue();
    });

    it('asks the browser whether autofill passkeys are available, and survives its refusal', async () => {
      statics.isConditionalMediationAvailable = () => Promise.resolve(true);
      expect(await service.isConditionalMediationAvailable()).toBeTrue();

      statics.isConditionalMediationAvailable = () => Promise.reject(new Error('no'));
      expect(await service.isConditionalMediationAvailable()).toBeFalse();

      statics.isConditionalMediationAvailable = undefined;
      expect(await service.isConditionalMediationAvailable()).toBeFalse();
    });

    it('creates with the native JSON parser and returns the credential as JSON', async () => {
      const parsed = { challenge: new ArrayBuffer(1) } as PublicKeyCredentialCreationOptions;
      statics.parseCreationOptionsFromJSON = jasmine
        .createSpy('parseCreationOptionsFromJSON')
        .and.returnValue(parsed);
      const createSpy = spyOn(navigator.credentials, 'create').and.resolveTo(
        serializableCredential({ id: 'CQk' }),
      );

      const result = await service.create(creationJson);

      expect(statics.parseCreationOptionsFromJSON).toHaveBeenCalledWith(creationJson);
      expect(createSpy).toHaveBeenCalledWith({ publicKey: parsed });
      expect(result).toEqual({ ok: true, credential: { id: 'CQk' } });
    });

    it('falls back to its own conversion where the browser has no JSON helpers', async () => {
      statics.parseCreationOptionsFromJSON = undefined;
      statics.parseRequestOptionsFromJSON = undefined;
      const createSpy = spyOn(navigator.credentials, 'create').and.resolveTo({
        type: 'public-key',
        id: 'CQk',
        rawId: new Uint8Array([9, 9]).buffer,
        authenticatorAttachment: null,
        response: {
          clientDataJSON: new Uint8Array([1]).buffer,
          attestationObject: new Uint8Array([2]).buffer,
          getTransports: () => ['internal'],
        },
        getClientExtensionResults: () => ({}),
      } as unknown as Credential);
      const getSpy = spyOn(navigator.credentials, 'get').and.resolveTo(
        serializableCredential({ id: 'got' }),
      );

      const created = await service.create(creationJson);
      const got = await service.get(requestJson);

      const options = createSpy.calls.mostRecent().args[0]!.publicKey!;
      expect(options.challenge).toEqual(jasmine.any(ArrayBuffer));
      expect(created.ok).toBeTrue();
      expect(created.ok && created.credential['rawId']).toBe('CQk');
      expect(getSpy.calls.mostRecent().args[0]!.publicKey!.challenge).toEqual(
        jasmine.any(ArrayBuffer),
      );
      expect(got).toEqual({ ok: true, credential: { id: 'got' } });
    });

    it('passes autofill mediation and an abort signal through to the browser', async () => {
      const parsed = { challenge: new ArrayBuffer(1) } as PublicKeyCredentialRequestOptions;
      statics.parseRequestOptionsFromJSON = () => parsed;
      const getSpy = spyOn(navigator.credentials, 'get').and.resolveTo(
        serializableCredential({ id: 'CQk' }),
      );
      const controller = new AbortController();

      await service.get(requestJson, { mediation: 'conditional', signal: controller.signal });
      await service.get(requestJson);

      expect(getSpy.calls.argsFor(0)[0]).toEqual({
        publicKey: parsed,
        mediation: 'conditional',
        signal: controller.signal,
      });
      expect(getSpy.calls.argsFor(1)[0]).toEqual({ publicKey: parsed });
    });

    const refusals: Array<[string, string]> = [
      ['NotAllowedError', 'cancelled'],
      ['AbortError', 'cancelled'],
      ['InvalidStateError', 'already_registered'],
      ['NotSupportedError', 'unsupported'],
      ['SecurityError', 'unsupported'],
      ['UnknownError', 'failed'],
    ];
    for (const [name, reason] of refusals) {
      it(`reports a ${name} as ${reason}`, async () => {
        spyOn(navigator.credentials, 'create').and.rejectWith(domError(name));

        expect(await service.create(creationJson)).toEqual({ ok: false, reason } as never);
      });
    }

    it('reports a thrown non-error as failed', async () => {
      spyOn(navigator.credentials, 'get').and.callFake(() => {
        throw 'boom';
      });

      expect(await service.get(requestJson)).toEqual({ ok: false, reason: 'failed' });
    });

    it('treats an empty or foreign credential as a dismissed prompt', async () => {
      const getSpy = spyOn(navigator.credentials, 'get').and.resolveTo(null);
      expect(await service.get(requestJson)).toEqual({ ok: false, reason: 'cancelled' });

      getSpy.and.resolveTo({ type: 'password', id: 'x' } as Credential);
      expect(await service.get(requestJson)).toEqual({ ok: false, reason: 'cancelled' });
    });

    it('reports a credential that will not serialize as failed', async () => {
      spyOn(navigator.credentials, 'get').and.resolveTo({
        type: 'public-key',
        id: 'x',
        toJSON: () => 'not an object',
      } as unknown as Credential);

      expect(await service.get(requestJson)).toEqual({ ok: false, reason: 'failed' });
    });
  });
});
