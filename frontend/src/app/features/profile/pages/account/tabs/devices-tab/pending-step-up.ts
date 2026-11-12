import { signal } from '@angular/core';

/**
 * An action refused for want of a fresh step-up, held while the user verifies.
 *
 * The user already confirmed the action, so once verification succeeds it runs again on its own
 * rather than asking them to find the button and confirm a second time.
 */
export class PendingStepUp {
  /** Whether the verification modal should be shown. */
  readonly open = signal(false);

  private action: (() => void) | null = null;

  request(action: () => void): void {
    this.action = action;
    this.open.set(true);
  }

  verified(): void {
    const action = this.action;
    this.action = null;
    this.open.set(false);
    action?.();
  }

  dismissed(): void {
    this.action = null;
    this.open.set(false);
  }
}
