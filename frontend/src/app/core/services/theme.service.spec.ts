import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { installMemoryStorage, installThrowingStorage } from '@testing';

import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let restoreStorage: () => void;
  let mediaListener: ((event: { matches: boolean }) => void) | null;

  function stubMatchMedia(matches: boolean): void {
    mediaListener = null;
    spyOn(window, 'matchMedia').and.returnValue({
      matches,
      addEventListener: (_type: string, listener: (event: { matches: boolean }) => void) => {
        mediaListener = listener;
      },
    } as unknown as MediaQueryList);
  }

  function createService(platform: string = 'browser'): ThemeService {
    TestBed.configureTestingModule({
      providers: [ThemeService, { provide: PLATFORM_ID, useValue: platform }],
    });

    return TestBed.inject(ThemeService);
  }

  beforeEach(() => {
    restoreStorage = installMemoryStorage('local');
    document.documentElement.classList.remove('dark');
  });

  afterEach(() => {
    restoreStorage();
    document.documentElement.classList.remove('dark');
  });

  describe('initial theme', () => {
    it('uses the stored choice over the system preference', () => {
      restoreStorage();
      restoreStorage = installMemoryStorage('local', { theme: 'dark' });
      stubMatchMedia(false);

      const service = createService();

      expect(service.theme()).toBe('dark');
      expect(service.isDark()).toBeTrue();
      expect(document.documentElement.classList.contains('dark')).toBeTrue();
    });

    it('falls back to the system preference when nothing is stored', () => {
      stubMatchMedia(true);

      expect(createService().theme()).toBe('dark');
    });

    it('defaults to light when nothing is stored and the system prefers light', () => {
      stubMatchMedia(false);

      expect(createService().theme()).toBe('light');
      expect(document.documentElement.classList.contains('dark')).toBeFalse();
    });

    it('ignores a stored value that is not a known theme', () => {
      restoreStorage();
      restoreStorage = installMemoryStorage('local', { theme: 'chartreuse' });
      stubMatchMedia(true);

      expect(createService().theme()).toBe('dark');
    });

    it('stays light on the server without touching the DOM', () => {
      stubMatchMedia(true);

      const service = createService('server');

      expect(service.theme()).toBe('light');
      expect(document.documentElement.classList.contains('dark')).toBeFalse();
    });
  });

  describe('set and toggle', () => {
    beforeEach(() => stubMatchMedia(false));

    it('applies, persists and reflects the chosen theme', () => {
      const service = createService();

      service.set('dark');

      expect(service.theme()).toBe('dark');
      expect(service.isDark()).toBeTrue();
      expect(localStorage.getItem('theme')).toBe('dark');
      expect(document.documentElement.classList.contains('dark')).toBeTrue();
    });

    it('flips between the two themes', () => {
      const service = createService();

      service.toggle();
      expect(service.theme()).toBe('dark');

      service.toggle();
      expect(service.theme()).toBe('light');
      expect(document.documentElement.classList.contains('dark')).toBeFalse();
    });

    it('swallows a storage write failure', () => {
      const service = createService();
      restoreStorage();
      restoreStorage = installThrowingStorage('local');

      expect(() => service.set('dark')).not.toThrow();
      expect(service.theme()).toBe('dark');
    });
  });

  describe('system preference listener', () => {
    it('follows later system changes while no choice is stored', () => {
      stubMatchMedia(false);
      const service = createService();

      expect(mediaListener).not.toBeNull();
      mediaListener?.({ matches: true });

      expect(service.theme()).toBe('dark');
    });

    it('stops following once the user makes an explicit choice', () => {
      stubMatchMedia(false);
      const service = createService();

      service.set('light');
      mediaListener?.({ matches: true });

      expect(service.theme()).toBe('light');
    });

    it('is not registered at all when a choice was already stored', () => {
      restoreStorage();
      restoreStorage = installMemoryStorage('local', { theme: 'light' });
      stubMatchMedia(true);

      createService();

      expect(mediaListener).toBeNull();
    });
  });
});
