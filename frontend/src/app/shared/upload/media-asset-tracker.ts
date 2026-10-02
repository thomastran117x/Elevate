import { signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { MediaAssetService } from './media-asset.service';

/** What an attached image is waiting on, as far as the editor that attached it can tell. */
export type MediaCheckState = 'checking' | 'slow';

export interface MediaCheckHandlers {
  /** The image is published at its URL; stop showing the local preview. */
  ready?: (publicUrl: string) => void;
  /** Validation refused the image; drop its tile and say why. */
  rejected?: (publicUrl: string, reason: string) => void;
}

export const MEDIA_CHECKING_LABEL = 'Checking image…';
export const MEDIA_SLOW_LABEL = 'Still processing — it will appear shortly';
export const MEDIA_REJECTED_FALLBACK = "This image couldn't be published.";

/**
 * Follows images an editor has attached until the server has validated them, keyed by public
 * URL like {@link LocalPreviews} so a template can ask about a tile by the URL it renders.
 *
 * A tile shows its local preview throughout, so the user never waits on — or sees a broken image
 * for — a public URL that has nothing behind it yet. The only thing this adds is a label while
 * the check runs, and a callback when it settles.
 *
 * The states live in a signal, so a template that asks for a label — including an OnPush child
 * the host hands {@link labelFor} to — re-renders when a check starts, slows, or settles.
 *
 * Owns its polls: call {@link stopAll} when the owning component is destroyed.
 */
export class MediaAssetTracker {
  private readonly states = signal<ReadonlyMap<string, MediaCheckState>>(new Map());
  private readonly polls = new Map<string, Subscription>();

  constructor(private readonly media: MediaAssetService) {}

  /**
   * Starts following an attached upload. Nothing happens without an asset id: uploads that are
   * not quarantined are published the moment they are attached.
   */
  watch(
    publicUrl: string,
    mediaAssetId: string | null | undefined,
    handlers: MediaCheckHandlers = {},
  ): void {
    if (!mediaAssetId) return;

    this.stop(publicUrl);
    this.setState(publicUrl, 'checking');

    const poll = this.media.watch(mediaAssetId).subscribe({
      next: (event) => {
        if (event.kind === 'slow') {
          this.setState(publicUrl, 'slow');
          return;
        }

        switch (event.asset.status) {
          case 'ready':
            this.stop(publicUrl);
            handlers.ready?.(publicUrl);
            break;
          case 'rejected':
            this.stop(publicUrl);
            handlers.rejected?.(publicUrl, event.asset.rejectionReason || MEDIA_REJECTED_FALLBACK);
            break;
          default:
            this.setState(publicUrl, 'checking');
        }
      },
      // A watch that ends without an answer (the asset vanished) simply stops labelling the tile.
      complete: () => {
        if (this.stateOf(publicUrl) === 'checking') this.stop(publicUrl);
      },
    });

    // A watch that settled synchronously has already cleaned up after itself.
    if (this.stateOf(publicUrl) !== null) this.polls.set(publicUrl, poll);
    else poll.unsubscribe();
  }

  stateOf(publicUrl: string): MediaCheckState | null {
    return this.states().get(publicUrl) ?? null;
  }

  /** The label to overlay on a tile, or null when there is nothing to wait for. */
  labelFor(publicUrl: string): string | null {
    const state = this.stateOf(publicUrl);
    if (state === 'checking') return MEDIA_CHECKING_LABEL;
    if (state === 'slow') return MEDIA_SLOW_LABEL;
    return null;
  }

  /** Stops following one image, after it is removed. */
  stop(publicUrl: string): void {
    this.polls.get(publicUrl)?.unsubscribe();
    this.polls.delete(publicUrl);
    if (!this.states().has(publicUrl)) return;

    const next = new Map(this.states());
    next.delete(publicUrl);
    this.states.set(next);
  }

  /** Stops every poll; call on destroy. */
  stopAll(): void {
    for (const publicUrl of [...this.polls.keys()]) this.stop(publicUrl);
    this.states.set(new Map());
  }

  private setState(publicUrl: string, state: MediaCheckState): void {
    if (this.stateOf(publicUrl) === state) return;
    this.states.update((states) => new Map(states).set(publicUrl, state));
  }
}
