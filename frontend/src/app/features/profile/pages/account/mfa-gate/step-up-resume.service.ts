import { Injectable } from '@angular/core';

const StepUpResumeStorageKey = 'step_up_resume_path';

/**
 * Account routes a step-up may send the user back to. Anything else, including query strings,
 * fragments, absolute or protocol-relative URLs, is refused so a stored value can never become
 * an open redirect.
 */
export const STEP_UP_RESUME_PATHS: readonly string[] = [
  '/account/profile',
  '/account/security',
  '/account/password',
  '/account/danger',
];

/**
 * Remembers which account tab asked for an in-session MFA step-up, so verification resumes
 * there. Backed by sessionStorage and guarded for SSR.
 */
@Injectable({ providedIn: 'root' })
export class StepUpResumeService {
  /** The allowlisted account path for a URL, or null when it is not a resumable route. */
  resolve(url: string | null | undefined): string | null {
    if (!url) {
      return null;
    }

    const trimmed = url.trim();
    if (!trimmed.startsWith('/') || trimmed.startsWith('//') || trimmed.startsWith('/\\')) {
      return null;
    }

    const path = trimmed.split(/[?#]/, 1)[0];
    return STEP_UP_RESUME_PATHS.includes(path) ? path : null;
  }

  remember(url: string): void {
    const path = this.resolve(url);
    if (!path || typeof sessionStorage === 'undefined') {
      return;
    }

    sessionStorage.setItem(StepUpResumeStorageKey, path);
  }

  consume(): string | null {
    if (typeof sessionStorage === 'undefined') {
      return null;
    }

    const value = sessionStorage.getItem(StepUpResumeStorageKey);
    sessionStorage.removeItem(StepUpResumeStorageKey);
    return this.resolve(value);
  }
}
