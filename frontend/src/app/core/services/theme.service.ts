import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

export type Theme = 'light' | 'dark';

const ThemeStorageKey = 'theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private platformId = inject(PLATFORM_ID);

  private readonly _theme = signal<Theme>('light');

  readonly theme = this._theme.asReadonly();
  readonly isDark = computed(() => this._theme() === 'dark');

  constructor() {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    const stored = this.readStored();
    const initial = stored ?? (this.prefersDark() ? 'dark' : 'light');
    this.apply(initial);

    // Follow the system preference until the user makes an explicit choice.
    if (!stored) {
      window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (event) => {
        if (!this.readStored()) {
          this.apply(event.matches ? 'dark' : 'light');
        }
      });
    }
  }

  toggle(): void {
    this.set(this._theme() === 'dark' ? 'light' : 'dark');
  }

  set(theme: Theme): void {
    this.apply(theme);
    this.persist(theme);
  }

  private apply(theme: Theme): void {
    this._theme.set(theme);

    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    document.documentElement.classList.toggle('dark', theme === 'dark');
  }

  private persist(theme: Theme): void {
    if (typeof localStorage === 'undefined') {
      return;
    }

    try {
      localStorage.setItem(ThemeStorageKey, theme);
    } catch {
      // Ignore storage failures (e.g. private mode quotas).
    }
  }

  private readStored(): Theme | null {
    if (typeof localStorage === 'undefined') {
      return null;
    }

    try {
      const value = localStorage.getItem(ThemeStorageKey);
      return value === 'dark' || value === 'light' ? value : null;
    } catch {
      return null;
    }
  }

  private prefersDark(): boolean {
    return (
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches
    );
  }
}
