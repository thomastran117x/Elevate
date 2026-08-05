import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';

import { fakeActivatedRoute, installMemoryStorage } from '@testing';

import { AuthReturnUrlService } from './auth-return-url.service';

const StorageKey = 'auth_return_url';

describe('AuthReturnUrlService', () => {
  let service: AuthReturnUrlService;
  let restoreStorage: () => void;

  beforeEach(() => {
    restoreStorage = installMemoryStorage('session');
    TestBed.configureTestingModule({ providers: [AuthReturnUrlService] });
    service = TestBed.inject(AuthReturnUrlService);
  });

  afterEach(() => {
    restoreStorage();
  });

  describe('set', () => {
    it('stores a same-origin relative path', () => {
      service.set('/events/1?tab=details#top');

      expect(sessionStorage.getItem(StorageKey)).toBe('/events/1?tab=details#top');
    });

    it('trims surrounding whitespace', () => {
      service.set('  /events/1  ');

      expect(sessionStorage.getItem(StorageKey)).toBe('/events/1');
    });

    it('reduces a same-origin absolute URL to path, search and hash', () => {
      service.set(`${window.location.origin}/clubs/2?tab=members#roster`);

      expect(sessionStorage.getItem(StorageKey)).toBe('/clubs/2?tab=members#roster');
    });

    // Open-redirect guard: each of these would otherwise send the user off-site
    // after login, carrying their freshly-issued session with them.
    const rejected = [
      ['a protocol-relative URL', '//evil.example.com/phish'],
      ['a backslash-escaped protocol-relative URL', '/\\evil.example.com/phish'],
      ['an absolute cross-origin URL', 'https://evil.example.com/phish'],
      ['a javascript: URL', 'javascript:alert(1)'],
      ['whitespace only', '   '],
      ['an empty string', ''],
    ] as const;

    for (const [label, url] of rejected) {
      it(`rejects ${label}`, () => {
        service.set(url);

        expect(sessionStorage.getItem(StorageKey)).toBeNull();
      });
    }

    it('rejects null and undefined', () => {
      service.set(null);
      service.set(undefined);

      expect(sessionStorage.getItem(StorageKey)).toBeNull();
    });
  });

  describe('peek', () => {
    it('returns the stored path without clearing it', () => {
      sessionStorage.setItem(StorageKey, '/events/1');

      expect(service.peek()).toBe('/events/1');
      expect(sessionStorage.getItem(StorageKey)).toBe('/events/1');
    });

    it('returns null when nothing is stored', () => {
      expect(service.peek()).toBeNull();
    });

    it('re-normalizes a hostile value that reached storage some other way', () => {
      sessionStorage.setItem(StorageKey, '//evil.example.com');

      expect(service.peek()).toBeNull();
    });
  });

  describe('consume', () => {
    it('returns the stored path and clears it', () => {
      sessionStorage.setItem(StorageKey, '/clubs/2');

      expect(service.consume()).toBe('/clubs/2');
      expect(sessionStorage.getItem(StorageKey)).toBeNull();
    });

    it('returns the default fallback when nothing is stored', () => {
      expect(service.consume()).toBe('/dashboard');
    });

    it('honours a caller-supplied fallback', () => {
      expect(service.consume('/account')).toBe('/account');
    });

    it('falls back — and still clears — when the stored value fails normalization', () => {
      sessionStorage.setItem(StorageKey, 'https://evil.example.com');

      expect(service.consume('/account')).toBe('/account');
      expect(sessionStorage.getItem(StorageKey)).toBeNull();
    });
  });

  describe('captureFromRoute', () => {
    it('stores the returnUrl query parameter', () => {
      const { route } = fakeActivatedRoute({ queryParams: { returnUrl: '/events/9' } });

      service.captureFromRoute(route);

      expect(sessionStorage.getItem(StorageKey)).toBe('/events/9');
    });

    it('does nothing when the route has no returnUrl', () => {
      const { route } = fakeActivatedRoute({ queryParams: { tab: 'details' } });

      service.captureFromRoute(route);

      expect(sessionStorage.getItem(StorageKey)).toBeNull();
    });

    it('applies the same open-redirect guard to route input', () => {
      const { route } = fakeActivatedRoute({
        queryParams: { returnUrl: 'https://evil.example.com' },
      });

      service.captureFromRoute(route as ActivatedRoute);

      expect(sessionStorage.getItem(StorageKey)).toBeNull();
    });
  });
});
