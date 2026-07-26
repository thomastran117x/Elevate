import { EventItem } from './event.types';

export type WaitlistEntryStatus = 'Waiting' | 'Promoted' | 'Left' | 'Removed';

export interface EventWaitlistEntry {
  id: number;
  eventId: number;
  userId: number;
  /** 1-based place in the queue; 0 for entries that are no longer waiting. */
  position: number;
  status: WaitlistEntryStatus;
  joinedAtUtc: string;
  promotedAtUtc?: string | null;
  leftAtUtc?: string | null;
  removedAtUtc?: string | null;
  /** Populated only for organizers who can manage the event. */
  userName?: string | null;
  userEmail?: string | null;
  notes?: string | null;
  phoneNumber?: string | null;
  dietaryNeeds?: string | null;
}

export interface MyWaitlistStatus {
  onWaitlist: boolean;
  entryId?: number | null;
  position?: number | null;
  joinedAtUtc?: string | null;
  waitlistCount: number;
}

export interface WaitlistedEvent {
  entryId: number;
  position: number;
  joinedAtUtc: string;
  /**
   * True when the user can no longer view the event (e.g. a revoked private-event invite).
   * `event` is then redacted to its id, but the row is still returned so they can withdraw.
   */
  accessRevoked: boolean;
  event: EventItem;
}

export interface JoinWaitlistDetails {
  notes?: string;
  phoneNumber?: string;
  dietaryNeeds?: string;
}

export interface WaitlistPromotionResult {
  promotedCount: number;
  promotedUserIds: number[];
}

export interface EventWaitlistPage {
  entries: EventWaitlistEntry[];
  totalCount: number;
}
