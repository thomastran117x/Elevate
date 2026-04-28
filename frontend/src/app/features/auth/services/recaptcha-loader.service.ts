import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class RecaptchaLoaderService {
  private loading: Promise<void> | null = null;

  load(siteKey: string): Promise<void> {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return Promise.reject(new Error('reCAPTCHA can only load in the browser.'));
    }

    if ((window as any).grecaptcha) {
      return Promise.resolve();
    }
    if (this.loading) return this.loading;

    this.loading = new Promise<void>((resolve, reject) => {
      const id = 'recaptcha-v3';
      if (document.getElementById(id)) {
        resolve();
        return;
      }

      const script = document.createElement('script');
      script.id = id;
      script.src = `https://www.google.com/recaptcha/api.js?render=${encodeURIComponent(siteKey)}`;
      script.async = true;
      script.defer = true;

      script.onload = () => resolve();
      script.onerror = () => reject(new Error('Failed to load reCAPTCHA script.'));

      document.head.appendChild(script);
    });

    return this.loading;
  }
}
