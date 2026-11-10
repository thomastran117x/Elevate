import { Injectable } from '@angular/core';

import { getServerErrorMessage } from '../../../core/api/models/api-client-error.model';
import { environment } from '../../../../environments/environment';

export const MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE =
  'Microsoft sign-in could not be completed. Please try again.';

/** The scope Microsoft sign-in authorizes with. */
export const MICROSOFT_SIGN_IN_SCOPE = 'openid profile email offline_access';

export interface MicrosoftTokenExchangeOptions {
  /** Defaults to the sign-in scope. */
  scope?: string;
  /**
   * The redirect URI the authorization request named, which Microsoft requires to match exactly.
   * Defaults to the sign-in callback this app builds; a flow the server started passes the URI
   * the server issued instead.
   */
  redirectUri?: string;
}

/**
 * Redeems a Microsoft authorization code in the browser. The app registration is a public SPA
 * client, which Microsoft lets redeem codes only from the browser, with the PKCE verifier that
 * began the flow. The resulting ID token is then verified by the backend.
 */
@Injectable({ providedIn: 'root' })
export class MicrosoftTokenExchangeService {
  async exchange(
    code: string,
    verifier: string,
    options: MicrosoftTokenExchangeOptions = {},
  ): Promise<string> {
    const body = new URLSearchParams({
      client_id: environment.msalClientId,
      grant_type: 'authorization_code',
      code,
      redirect_uri: options.redirectUri ?? `${environment.frontendUrl}/auth/microsoft`,
      code_verifier: verifier,
      scope: options.scope ?? MICROSOFT_SIGN_IN_SCOPE,
    });

    let response: Response;
    try {
      response = await fetch('https://login.microsoftonline.com/common/oauth2/v2.0/token', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString(),
      });
    } catch {
      throw new Error(getServerErrorMessage(0));
    }

    const payload = (await readJsonPayload(response)) as { id_token?: unknown } | null;
    if (!response.ok) {
      throw new Error(
        response.status >= 500
          ? getServerErrorMessage(response.status)
          : MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE,
      );
    }

    if (typeof payload?.id_token !== 'string' || !payload.id_token) {
      throw new Error(MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE);
    }

    return payload.id_token;
  }
}

async function readJsonPayload(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
