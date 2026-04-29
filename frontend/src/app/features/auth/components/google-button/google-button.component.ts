import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { environment } from '../../../../../environments/environment';

@Component({
  selector: 'app-google-button',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './google-button.component.html',
  styleUrls: ['./google-button.component.css'],
})
export class GoogleButtonComponent {
  private static readonly CodeVerifierStorageKey = 'google_code_verifier';
  private static readonly StateStorageKey = 'google_oauth_state';
  private static readonly NonceStorageKey = 'google_oauth_nonce';

  loading = false;

  async loginWithGoogle(): Promise<void> {
    try {
      this.loading = true;

      const scope = 'openid email profile';
      const state = crypto.randomUUID();
      const nonce = crypto.randomUUID();
      const codeVerifier = crypto.randomUUID() + crypto.randomUUID();
      const codeChallenge = await this.generateCodeChallenge(codeVerifier);

      sessionStorage.setItem(GoogleButtonComponent.CodeVerifierStorageKey, codeVerifier);
      sessionStorage.setItem(GoogleButtonComponent.StateStorageKey, state);
      sessionStorage.setItem(GoogleButtonComponent.NonceStorageKey, nonce);

      const params = new URLSearchParams({
        client_id: environment.googleClientId,
        redirect_uri: `${environment.frontendUrl}/auth/google`,
        response_type: 'code',
        scope,
        state,
        nonce,
        code_challenge: codeChallenge,
        code_challenge_method: 'S256',
        prompt: 'select_account',
      });

      const googleAuthUrl = `https://accounts.google.com/o/oauth2/v2/auth?${params.toString()}`;
      window.location.href = googleAuthUrl;
    } catch (error) {
      console.error('Failed to start Google OAuth flow:', error);
    } finally {
      this.loading = false;
    }
  }

  private async generateCodeChallenge(verifier: string): Promise<string> {
    const encoder = new TextEncoder();
    const digest = await crypto.subtle.digest('SHA-256', encoder.encode(verifier));
    return this.base64UrlEncode(digest);
  }

  private base64UrlEncode(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let value = '';
    for (const byte of bytes) value += String.fromCharCode(byte);

    return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
}
