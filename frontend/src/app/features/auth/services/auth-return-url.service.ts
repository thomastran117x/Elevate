import { Injectable } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

const ReturnUrlStorageKey = 'auth_return_url';

@Injectable({ providedIn: 'root' })
export class AuthReturnUrlService {
  captureFromRoute(route: ActivatedRoute): void {
    const returnUrl = route.snapshot.queryParamMap.get('returnUrl');
    if (returnUrl) {
      this.set(returnUrl);
    }
  }

  set(url: string | null | undefined): void {
    const normalized = this.normalize(url);
    if (!normalized || typeof sessionStorage === 'undefined') {
      return;
    }

    sessionStorage.setItem(ReturnUrlStorageKey, normalized);
  }

  peek(): string | null {
    if (typeof sessionStorage === 'undefined') {
      return null;
    }

    const value = sessionStorage.getItem(ReturnUrlStorageKey);
    return this.normalize(value);
  }

  consume(fallback = '/dashboard'): string {
    if (typeof sessionStorage === 'undefined') {
      return fallback;
    }

    const value = sessionStorage.getItem(ReturnUrlStorageKey);
    if (value) {
      sessionStorage.removeItem(ReturnUrlStorageKey);
      return this.normalize(value) ?? fallback;
    }

    return fallback;
  }

  private normalize(url: string | null | undefined): string | null {
    if (!url) {
      return null;
    }

    const trimmed = url.trim();
    if (!trimmed) {
      return null;
    }

    if (trimmed.startsWith('/')) {
      return trimmed.startsWith('//') ? null : trimmed;
    }

    if (typeof window !== 'undefined') {
      try {
        const parsed = new URL(trimmed, window.location.origin);
        if (parsed.origin === window.location.origin) {
          return `${parsed.pathname}${parsed.search}${parsed.hash}`;
        }
      } catch {
        return null;
      }
    }

    return null;
  }
}
