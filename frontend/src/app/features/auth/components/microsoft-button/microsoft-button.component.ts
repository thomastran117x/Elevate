import { Component } from '@angular/core';

import { environment } from '../../../../../environments/environment';

@Component({
  selector: 'app-microsoft-button',
  standalone: true,
  imports: [],
  templateUrl: './microsoft-button.component.html',
  styleUrls: ['./microsoft-button.component.css'],
})
export class MicrosoftButtonComponent {
  private static readonly CodeVerifierStorageKey = 'ms_code_verifier';
  private static readonly StateStorageKey = 'ms_oauth_state';
  private static readonly NonceStorageKey = 'ms_oauth_nonce';

  async loginWithMicrosoft() {
    const codeVerifier = crypto.randomUUID() + crypto.randomUUID();
    const state = crypto.randomUUID();
    const nonce = crypto.randomUUID();
    const codeChallenge = await this.generateCodeChallenge(codeVerifier);
    sessionStorage.setItem(MicrosoftButtonComponent.CodeVerifierStorageKey, codeVerifier);
    sessionStorage.setItem(MicrosoftButtonComponent.StateStorageKey, state);
    sessionStorage.setItem(MicrosoftButtonComponent.NonceStorageKey, nonce);

    const authUrl =
      'https://login.microsoftonline.com/common/oauth2/v2.0/authorize' +
      `?client_id=${environment.msalClientId}` +
      `&response_type=code` +
      `&redirect_uri=${encodeURIComponent(`${environment.frontendUrl}/auth/microsoft`)}` +
      `&response_mode=query` +
      `&scope=openid profile email offline_access` +
      `&state=${encodeURIComponent(state)}` +
      `&nonce=${encodeURIComponent(nonce)}` +
      `&code_challenge=${codeChallenge}` +
      `&code_challenge_method=S256`;

    window.location.href = authUrl;
  }

  private async generateCodeChallenge(verifier: string): Promise<string> {
    const encoder = new TextEncoder();
    const data = encoder.encode(verifier);
    const digest = await crypto.subtle.digest('SHA-256', data);
    return this.base64URLEncode(digest);
  }

  private base64URLEncode(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let str = '';
    for (const b of bytes) str += String.fromCharCode(b);
    return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
}
