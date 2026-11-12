import {
  base64UrlToBuffer,
  bufferToBase64Url,
  credentialToJson,
  toCreationOptions,
  toRequestOptions,
} from './webauthn-json';

function bytes(...values: number[]): ArrayBuffer {
  return new Uint8Array(values).buffer;
}

function asArray(buffer: BufferSource | undefined): number[] {
  if (!buffer) {
    return [];
  }
  return Array.from(
    buffer instanceof ArrayBuffer
      ? new Uint8Array(buffer)
      : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength),
  );
}

describe('webauthn-json', () => {
  it('round-trips base64url without padding, including the URL-safe characters', () => {
    const original = [0xfb, 0xff, 0x00, 0x3e, 0x3f];

    const encoded = bufferToBase64Url(bytes(...original));

    expect(encoded).toBe('-_8APj8');
    expect(encoded).not.toContain('=');
    expect(asArray(base64UrlToBuffer(encoded))).toEqual(original);
    expect(bufferToBase64Url(new Uint8Array([1, 2, 3, 4]).subarray(1, 3))).toBe('AgM');
  });

  it('decodes the binary members of creation options and keeps the rest', () => {
    const options = toCreationOptions({
      rp: { id: 'localhost', name: 'EventXperience' },
      user: { id: 'AQID', name: 'owner', displayName: 'Owner' },
      challenge: 'AAEC',
      pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      authenticatorSelection: { residentKey: 'required', userVerification: 'required' },
      excludeCredentials: [{ type: 'public-key', id: 'CQk', transports: ['internal'] }],
    });

    expect(asArray(options.challenge)).toEqual([0, 1, 2]);
    expect(asArray(options.user.id)).toEqual([1, 2, 3]);
    expect(options.user.displayName).toBe('Owner');
    expect(options.rp.id).toBe('localhost');
    expect(options.authenticatorSelection?.userVerification).toBe('required');
    expect(asArray(options.excludeCredentials?.[0].id)).toEqual([9, 9]);
    expect(options.excludeCredentials?.[0].transports).toEqual(['internal']);
  });

  it('defaults a missing display name and exclusion list', () => {
    const options = toCreationOptions({ challenge: 'AA', user: { id: 'AQ', name: 'owner' } });

    expect(options.user.displayName).toBe('');
    expect(options.excludeCredentials).toEqual([]);
  });

  it('refuses options without a challenge or user id', () => {
    expect(() => toCreationOptions({ user: { id: 'AQ', name: 'owner' } })).toThrowError(
      /challenge/,
    );
    expect(() => toCreationOptions({ challenge: 'AA', user: { name: 'owner' } })).toThrowError(
      /id/,
    );
    expect(() => toRequestOptions(null)).toThrowError(/challenge/);
  });

  it('decodes the binary members of request options', () => {
    const options = toRequestOptions({
      challenge: 'AAEC',
      rpId: 'localhost',
      userVerification: 'required',
      allowCredentials: [{ type: 'public-key', id: 'CQk' }],
    });

    expect(asArray(options.challenge)).toEqual([0, 1, 2]);
    expect(options.rpId).toBe('localhost');
    expect(asArray(options.allowCredentials?.[0].id)).toEqual([9, 9]);
    expect(toRequestOptions({ challenge: 'AA' }).allowCredentials).toEqual([]);
  });

  it('encodes an attestation for the API', () => {
    const credential = {
      id: 'CQk',
      rawId: bytes(9, 9),
      type: 'public-key',
      authenticatorAttachment: 'platform',
      response: {
        clientDataJSON: bytes(1),
        attestationObject: bytes(2),
        getTransports: () => ['internal', 'hybrid'],
      },
      getClientExtensionResults: () => ({ credProps: { rk: true } }),
    } as unknown as PublicKeyCredential;

    expect(credentialToJson(credential)).toEqual({
      id: 'CQk',
      rawId: 'CQk',
      type: 'public-key',
      authenticatorAttachment: 'platform',
      response: {
        clientDataJSON: 'AQ',
        attestationObject: 'Ag',
        transports: ['internal', 'hybrid'],
      },
      clientExtensionResults: { credProps: { rk: true } },
    });
  });

  it('encodes an assertion for the API, with or without a user handle', () => {
    const assertion = (userHandle: ArrayBuffer | null) =>
      ({
        id: 'CQk',
        rawId: bytes(9, 9),
        type: 'public-key',
        authenticatorAttachment: null,
        response: {
          clientDataJSON: bytes(1),
          authenticatorData: bytes(3),
          signature: bytes(4),
          userHandle,
        },
        getClientExtensionResults: () => ({}),
      }) as unknown as PublicKeyCredential;

    expect(credentialToJson(assertion(bytes(5)))['response']).toEqual({
      clientDataJSON: 'AQ',
      authenticatorData: 'Aw',
      signature: 'BA',
      userHandle: 'BQ',
    });
    const withoutHandle = credentialToJson(assertion(null));
    expect(withoutHandle['response']).toEqual({
      clientDataJSON: 'AQ',
      authenticatorData: 'Aw',
      signature: 'BA',
    });
    expect(withoutHandle['authenticatorAttachment']).toBeNull();
  });

  it('sends no transports when the browser cannot list them', () => {
    const credential = {
      id: 'CQk',
      rawId: bytes(9, 9),
      type: 'public-key',
      response: { clientDataJSON: bytes(1), attestationObject: bytes(2) },
      getClientExtensionResults: () => ({}),
    } as unknown as PublicKeyCredential;

    expect(credentialToJson(credential)['response']).toEqual(
      jasmine.objectContaining({ transports: [] }),
    );
  });
});
