import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';

import { credentialToJson, toCreationOptions, toRequestOptions } from './webauthn-json';

/**
 * Why a ceremony produced no credential. Callers word their own message; none of these is an
 * authentication failure, which only the server decides.
 */
export type WebAuthnFailureReason =
  /** The user dismissed the prompt, it timed out, or no matching passkey was chosen. */
  | 'cancelled'
  /** The authenticator already holds a passkey for this account (excludeCredentials). */
  | 'already_registered'
  /** This browser, or this page (SSR, insecure context), cannot run WebAuthn. */
  | 'unsupported'
  /** Anything else the browser refused. */
  | 'failed';

export type WebAuthnResult =
  { ok: true; credential: Record<string, unknown> } | { ok: false; reason: WebAuthnFailureReason };

export interface WebAuthnGetOptions {
  /** `conditional` offers passkeys in the username field's autofill instead of a dialog. */
  mediation?: CredentialMediationRequirement;
  signal?: AbortSignal;
}

/** The static JSON helpers from WebAuthn Level 3, where the browser has them. */
interface PublicKeyCredentialJsonStatics {
  parseCreationOptionsFromJSON?: (options: unknown) => PublicKeyCredentialCreationOptions;
  parseRequestOptionsFromJSON?: (options: unknown) => PublicKeyCredentialRequestOptions;
  isConditionalMediationAvailable?: () => Promise<boolean>;
}

interface JsonSerializableCredential {
  toJSON?: () => unknown;
}

/**
 * Runs WebAuthn ceremonies in the browser. Every method is safe to call during server rendering:
 * it reports `unsupported` instead of touching `window` or `navigator`.
 */
@Injectable({ providedIn: 'root' })
export class WebAuthnService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  isSupported(): boolean {
    return (
      this.isBrowser &&
      typeof window !== 'undefined' &&
      window.isSecureContext !== false &&
      typeof window.PublicKeyCredential === 'function' &&
      typeof navigator !== 'undefined' &&
      typeof navigator.credentials?.create === 'function' &&
      typeof navigator.credentials?.get === 'function'
    );
  }

  /** Whether passkeys can be offered in the username field's autofill. */
  async isConditionalMediationAvailable(): Promise<boolean> {
    if (!this.isSupported()) {
      return false;
    }

    const statics = window.PublicKeyCredential as unknown as PublicKeyCredentialJsonStatics;
    if (typeof statics.isConditionalMediationAvailable !== 'function') {
      return false;
    }

    try {
      return await statics.isConditionalMediationAvailable();
    } catch {
      return false;
    }
  }

  /** Runs `navigator.credentials.create()` for the API's creation options JSON. */
  async create(optionsJson: unknown): Promise<WebAuthnResult> {
    if (!this.isSupported()) {
      return { ok: false, reason: 'unsupported' };
    }

    try {
      const statics = window.PublicKeyCredential as unknown as PublicKeyCredentialJsonStatics;
      const publicKey =
        typeof statics.parseCreationOptionsFromJSON === 'function'
          ? statics.parseCreationOptionsFromJSON(optionsJson)
          : toCreationOptions(optionsJson);
      const credential = await navigator.credentials.create({ publicKey });
      return this.toResult(credential);
    } catch (error) {
      return { ok: false, reason: this.classify(error) };
    }
  }

  /** Runs `navigator.credentials.get()` for the API's request options JSON. */
  async get(optionsJson: unknown, options: WebAuthnGetOptions = {}): Promise<WebAuthnResult> {
    if (!this.isSupported()) {
      return { ok: false, reason: 'unsupported' };
    }

    try {
      const statics = window.PublicKeyCredential as unknown as PublicKeyCredentialJsonStatics;
      const publicKey =
        typeof statics.parseRequestOptionsFromJSON === 'function'
          ? statics.parseRequestOptionsFromJSON(optionsJson)
          : toRequestOptions(optionsJson);
      const credential = await navigator.credentials.get({
        publicKey,
        ...(options.mediation ? { mediation: options.mediation } : {}),
        ...(options.signal ? { signal: options.signal } : {}),
      });
      return this.toResult(credential);
    } catch (error) {
      return { ok: false, reason: this.classify(error) };
    }
  }

  private toResult(credential: Credential | null): WebAuthnResult {
    if (!credential || credential.type !== 'public-key') {
      return { ok: false, reason: 'cancelled' };
    }

    const publicKeyCredential = credential as PublicKeyCredential;
    const serializable = publicKeyCredential as unknown as JsonSerializableCredential;
    const json =
      typeof serializable.toJSON === 'function'
        ? serializable.toJSON()
        : credentialToJson(publicKeyCredential);

    return typeof json === 'object' && json !== null
      ? { ok: true, credential: json as Record<string, unknown> }
      : { ok: false, reason: 'failed' };
  }

  private classify(error: unknown): WebAuthnFailureReason {
    const name = error instanceof Error || error instanceof DOMException ? error.name : '';
    switch (name) {
      case 'NotAllowedError':
      case 'AbortError':
        return 'cancelled';
      case 'InvalidStateError':
        return 'already_registered';
      case 'NotSupportedError':
      case 'SecurityError':
        return 'unsupported';
      default:
        return 'failed';
    }
  }
}
