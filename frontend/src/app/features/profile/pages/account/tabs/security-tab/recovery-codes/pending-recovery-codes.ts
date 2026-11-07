import { Injectable, signal } from '@angular/core';

/**
 * A newly generated recovery-code set the user has not yet confirmed saving.
 *
 * Provided by the security tab rather than held by the card: the tab tears its content down
 * whenever it re-locks for a missing step-up proof, which generating codes itself causes, and the
 * server never returns the codes again. Kept here, the set is shown again once the user verifies.
 */
@Injectable()
export class PendingRecoveryCodes {
  readonly codes = signal<string[] | null>(null);
  readonly acknowledged = signal(false);

  show(codes: string[]): void {
    this.codes.set(codes);
    this.acknowledged.set(false);
  }

  clear(): void {
    this.codes.set(null);
    this.acknowledged.set(false);
  }
}
