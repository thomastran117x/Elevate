import { EventItem } from './event.types';

export interface EventFavourite {
  eventId: number;
  isFavourited: boolean;
  favouritedAtUtc: string | null;
}

/**
 * One row of the pinned list — the union of the events the user registered for and the events
 * they starred. `isRegistered` / `isFavourited` say which signal (or both) put it there.
 */
export interface PinnedEvent {
  isRegistered: boolean;
  isFavourited: boolean;
  favouritedAtUtc: string | null;
  registeredAtUtc: string | null;
  /**
   * True when the user can no longer view the event (e.g. a revoked private-event invite).
   * `event` is then redacted to its id, but the row is still returned so they can unstar it.
   */
  accessRevoked: boolean;
  event: EventItem;
}
