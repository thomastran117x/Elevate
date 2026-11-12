/**
 * Converts between the WebAuthn JSON the API speaks (binary values as base64url strings) and the
 * ArrayBuffer-based objects `navigator.credentials` takes and returns. Browsers that implement
 * `PublicKeyCredential.parse*OptionsFromJSON()` and `toJSON()` do this natively; this module is
 * the fallback for those that do not. It only re-encodes fields; it never interprets them, and
 * every check is made by the server.
 */

type JsonObject = Record<string, unknown>;

export function base64UrlToBuffer(value: string): ArrayBuffer {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes.buffer;
}

export function bufferToBase64Url(value: ArrayBuffer | ArrayBufferView): string {
  const bytes =
    value instanceof ArrayBuffer
      ? new Uint8Array(value)
      : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function asObject(value: unknown): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as JsonObject)
    : {};
}

function requireString(source: JsonObject, key: string): string {
  const value = source[key];
  if (typeof value !== 'string' || value.length === 0) {
    throw new TypeError(`WebAuthn options are missing "${key}".`);
  }
  return value;
}

function toDescriptors(value: unknown): PublicKeyCredentialDescriptor[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((entry) => {
    const descriptor = asObject(entry);
    const transports = descriptor['transports'];
    return {
      type: 'public-key',
      id: base64UrlToBuffer(requireString(descriptor, 'id')),
      ...(Array.isArray(transports) ? { transports: transports as AuthenticatorTransport[] } : {}),
    };
  });
}

/** WebAuthn creation options JSON to what `navigator.credentials.create()` takes. */
export function toCreationOptions(json: unknown): PublicKeyCredentialCreationOptions {
  const source = asObject(json);
  const user = asObject(source['user']);

  return {
    ...(source as unknown as PublicKeyCredentialCreationOptions),
    challenge: base64UrlToBuffer(requireString(source, 'challenge')),
    user: {
      id: base64UrlToBuffer(requireString(user, 'id')),
      name: requireString(user, 'name'),
      displayName: typeof user['displayName'] === 'string' ? user['displayName'] : '',
    },
    excludeCredentials: toDescriptors(source['excludeCredentials']),
  };
}

/** WebAuthn request options JSON to what `navigator.credentials.get()` takes. */
export function toRequestOptions(json: unknown): PublicKeyCredentialRequestOptions {
  const source = asObject(json);

  return {
    ...(source as unknown as PublicKeyCredentialRequestOptions),
    challenge: base64UrlToBuffer(requireString(source, 'challenge')),
    allowCredentials: toDescriptors(source['allowCredentials']),
  };
}

/** A credential from `navigator.credentials` to the WebAuthn JSON the API takes. */
export function credentialToJson(credential: PublicKeyCredential): JsonObject {
  const response = credential.response;
  const body: JsonObject = {
    clientDataJSON: bufferToBase64Url(response.clientDataJSON),
  };

  if ('attestationObject' in response) {
    const attestation = response as AuthenticatorAttestationResponse;
    body['attestationObject'] = bufferToBase64Url(attestation.attestationObject);
    body['transports'] =
      typeof attestation.getTransports === 'function' ? attestation.getTransports() : [];
  } else {
    const assertion = response as AuthenticatorAssertionResponse;
    body['authenticatorData'] = bufferToBase64Url(assertion.authenticatorData);
    body['signature'] = bufferToBase64Url(assertion.signature);
    if (assertion.userHandle) {
      body['userHandle'] = bufferToBase64Url(assertion.userHandle);
    }
  }

  return {
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: body,
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
    clientExtensionResults: credential.getClientExtensionResults(),
  };
}
