import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';

export type EventCategory =
  | 'Sports'
  | 'Music'
  | 'Academic'
  | 'Workshop'
  | 'Conference'
  | 'Social'
  | 'Cultural'
  | 'Gaming'
  | 'Food'
  | 'Fitness'
  | 'Networking'
  | 'Volunteer'
  | 'Party'
  | 'Arts'
  | 'Other';

export type EventStatus = 'Upcoming' | 'Ongoing' | 'Closed';
export type EventLifecycleState = 'Draft' | 'Published' | 'Cancelled' | 'Archived' | 'Paused';

export type EventSortBy = 'Relevance' | 'Date' | 'Distance' | 'Popularity';
export type ClubType = 'Sports' | 'Academic' | 'Social' | 'Cultural' | 'Gaming' | 'Other';

export interface EventHostClub {
  id: number;
  name: string;
  description: string;
  clubType: ClubType;
  clubImage: string;
  memberCount: number;
  eventCount: number;
  availableEventCount: number;
  isPrivate: boolean;
  email?: string;
  phone?: string;
  rating?: number;
  websiteUrl?: string;
  location?: string;
}

export interface EventItem {
  id: number;
  name: string;
  description: string;
  location: string;
  imageUrls: string[];
  isPrivate: boolean;
  maxParticipants: number;
  registerCost: number;
  startTime: string;
  endTime?: string;
  clubId: number;
  createdAt: string;
  lifecycleState: EventLifecycleState;
  status: EventStatus;
  category: EventCategory;
  venueName?: string;
  city?: string;
  latitude?: number;
  longitude?: number;
  tags: string[];
  registrationCount: number;
  waitlistEnabled: boolean;
  waitlistCount: number;
  distanceKm?: number;
  club?: EventHostClub;
}

export interface EventsPagedData {
  items: EventItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ManagedEvent {
  id: number;
  name?: string;
  description?: string;
  location?: string;
  imageUrls: string[];
  isPrivate: boolean;
  maxParticipants?: number;
  registerCost: number;
  startTime?: string;
  endTime?: string;
  clubId: number;
  currentVersionNumber: number;
  createdAt: string;
  updatedAt: string;
  status?: EventStatus;
  lifecycleState: EventLifecycleState;
  category: EventCategory;
  venueName?: string;
  city?: string;
  latitude?: number;
  longitude?: number;
  tags: string[];
  registrationCount: number;
  waitlistEnabled: boolean;
  waitlistCount: number;
  publishReady: boolean;
  publishIssues: string[];
  seriesId?: number | null;
  occurrenceIndex?: number | null;
  seriesOverridden?: boolean;
  timeZoneId?: string | null;
  lifecycleChangedAt?: string | null;
  previousLifecycleState?: EventLifecycleState | null;
  /** Deadline for undoing the last lifecycle change; absent when there is nothing to undo. */
  revertAvailableUntil?: string | null;
  availableTransitions: EventLifecycleTransition[];
}

export interface ManagedEventsPagedData {
  items: ManagedEvent[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type ManagedEventsApiResponse = ApiEnvelope<ManagedEventsPagedData, EventsResponseMeta>;
export type ManagedEventApiResponse = ApiEnvelope<ManagedEvent, EventsResponseMeta>;

export interface EventDraftPayload {
  name?: string;
  description?: string;
  location?: string;
  imageUrls?: string[];
  isPrivate?: boolean;
  maxParticipants?: number;
  registerCost?: number;
  waitlistEnabled?: boolean;
  startTime?: string;
  endTime?: string | null;
  category?: EventCategory;
  venueName?: string | null;
  city?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  tags?: string[];
}

export interface EventsResponseMeta extends Record<string, unknown> {
  source?: string;
}

export type EventsApiResponse = ApiEnvelope<EventsPagedData, EventsResponseMeta>;
export type EventApiResponse = ApiEnvelope<EventItem, EventsResponseMeta>;

export interface EventSearchParams {
  search?: string;
  city?: string;
  category?: EventCategory;
  status?: EventStatus;
  sortBy?: EventSortBy;
  tags?: string;
  lat?: number;
  lng?: number;
  radiusKm?: number;
  page?: number;
  pageSize?: number;
}

export interface ManageEventsParams {
  lifecycleState?: EventLifecycleState | null;
  page?: number;
  pageSize?: number;
  search?: string;
}

export const ALL_CATEGORIES: EventCategory[] = [
  'Sports',
  'Music',
  'Academic',
  'Workshop',
  'Conference',
  'Social',
  'Cultural',
  'Gaming',
  'Food',
  'Fitness',
  'Networking',
  'Volunteer',
  'Party',
  'Arts',
  'Other',
];

export const ALL_STATUSES: EventStatus[] = ['Upcoming', 'Ongoing', 'Closed'];
/**
 * Order is load-bearing. The API has no string-enum converter, so lifecycle states arrive as
 * integers and are decoded by index against this array. Only ever append.
 */
export const ALL_LIFECYCLE_STATES: EventLifecycleState[] = [
  'Draft',
  'Published',
  'Cancelled',
  'Archived',
  'Paused',
];

/**
 * A lifecycle move the server says is currently available, with the copy for its confirmation
 * prompt already resolved against the event's real numbers. Rendering straight from these keeps
 * the state machine in one place, on the server.
 */
export interface EventLifecycleTransition {
  /** Endpoint segment for the action, e.g. `pause`. */
  key: string;
  target: EventLifecycleState;
  label: string;
  title: string;
  isReversible: boolean;
  reversibleNote?: string | null;
  isDestructive: boolean;
  impacts: string[];
}

export const ALL_EVENT_SORTS: EventSortBy[] = ['Relevance', 'Date', 'Distance', 'Popularity'];

export const CATEGORY_STYLES: Record<EventCategory, { badge: string; bg: string }> = {
  Sports: {
    badge: 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 border-emerald-500/20',
    bg: 'bg-emerald-500/60',
  },
  Music: {
    badge: 'bg-sky-500/10 text-sky-700 dark:text-sky-300 border-sky-500/20',
    bg: 'bg-sky-500/60',
  },
  Academic: {
    badge: 'bg-slate-500/10 text-slate-700 dark:text-slate-300 border-slate-500/20',
    bg: 'bg-slate-500/60',
  },
  Workshop: {
    badge: 'bg-cyan-500/10 text-cyan-700 dark:text-cyan-300 border-cyan-500/20',
    bg: 'bg-cyan-500/60',
  },
  Conference: {
    badge: 'bg-stone-500/10 text-stone-700 dark:text-stone-300 border-stone-500/20',
    bg: 'bg-stone-500/60',
  },
  Social: {
    badge: 'bg-amber-500/10 text-amber-700 dark:text-amber-300 border-amber-500/20',
    bg: 'bg-amber-500/60',
  },
  Cultural: {
    badge: 'bg-orange-500/10 text-orange-700 dark:text-orange-300 border-orange-500/20',
    bg: 'bg-orange-500/60',
  },
  Gaming: {
    badge: 'bg-teal-500/10 text-teal-700 dark:text-teal-300 border-teal-500/20',
    bg: 'bg-teal-500/60',
  },
  Food: {
    badge: 'bg-lime-500/10 text-lime-700 dark:text-lime-300 border-lime-500/20',
    bg: 'bg-lime-500/60',
  },
  Fitness: {
    badge: 'bg-rose-500/10 text-rose-700 dark:text-rose-300 border-rose-500/20',
    bg: 'bg-rose-500/60',
  },
  Networking: {
    badge: 'bg-blue-500/10 text-blue-700 dark:text-blue-300 border-blue-500/20',
    bg: 'bg-blue-500/60',
  },
  Volunteer: {
    badge: 'bg-green-500/10 text-green-700 dark:text-green-300 border-green-500/20',
    bg: 'bg-green-500/60',
  },
  Party: {
    badge: 'bg-red-500/10 text-red-700 dark:text-red-300 border-red-500/20',
    bg: 'bg-red-500/60',
  },
  Arts: {
    badge: 'bg-yellow-500/10 text-yellow-700 dark:text-yellow-300 border-yellow-500/20',
    bg: 'bg-yellow-500/60',
  },
  Other: {
    badge: 'bg-zinc-500/10 text-zinc-700 dark:text-zinc-300 border-zinc-500/20',
    bg: 'bg-zinc-500/60',
  },
};

// ── Recurrence series ────────────────────────────────────────────────────────

export type RecurrenceFrequency = 'Daily' | 'Weekly' | 'Monthly';
export type RecurrenceEndMode = 'Count' | 'UntilDate';
export type MonthlyDayPolicy = 'ClampToMonthEnd' | 'SkipMissingMonths';
export type EventSeriesStatus = 'Active' | 'Cancelled';
export type EventSeriesDeleteScope = 'SeriesRecordOnly' | 'FutureDrafts' | 'AllUnregistered';

/** Which occurrences an edit should apply to. */
export type OccurrenceEditScope = 'this' | 'thisAndFollowing';

export const ALL_RECURRENCE_FREQUENCIES: RecurrenceFrequency[] = ['Daily', 'Weekly', 'Monthly'];

/**
 * A repeat rule. `startLocalDateTime` is a bare wall-clock string (`2026-03-03T19:00`) paired
 * with `timeZoneId` — never an ISO instant. Converting it through the browser's zone is exactly
 * the bug the time zone selector exists to prevent.
 */
export interface RecurrenceRule {
  frequency: RecurrenceFrequency;
  interval: number;
  byWeekdays?: number[];
  monthlyDayPolicy: MonthlyDayPolicy;
  startLocalDateTime: string;
  durationMinutes?: number | null;
  timeZoneId: string;
  endMode: RecurrenceEndMode;
  endLocalDate?: string | null;
  occurrenceCount?: number | null;
}

export interface OccurrencePreview {
  index: number;
  localStart: string;
  startTimeUtc: string;
  endTimeUtc?: string | null;
  utcOffset: string;
  wasInvalidLocalTime: boolean;
  wasAmbiguousLocalTime: boolean;
}

export interface SeriesPreview {
  timeZoneId: string;
  occurrenceCount: number;
  occurrences: OccurrencePreview[];
  warnings: string[];
}

export interface EventSeriesRule {
  frequency: RecurrenceFrequency;
  interval: number;
  byWeekdays: number[];
  monthlyDayPolicy: MonthlyDayPolicy;
  timeZoneId: string;
  firstOccurrenceLocalStart: string;
  durationMinutes?: number | null;
  endMode: RecurrenceEndMode;
  endLocalDate?: string | null;
  occurrenceCount?: number | null;
}

export interface EventSeries {
  id: number;
  clubId: number;
  templateEventId?: number | null;
  status: EventSeriesStatus;
  generatedCount: number;
  rule: EventSeriesRule;
  createdAt: string;
  updatedAt: string;
  occurrences: ManagedEvent[];
  warnings: string[];
}

export interface EventSeriesSummary {
  id: number;
  clubId: number;
  templateEventId?: number | null;
  status: EventSeriesStatus;
  generatedCount: number;
  rule: EventSeriesRule;
  createdAt: string;
  updatedAt: string;
  name?: string | null;
  nextOccurrenceUtc?: string | null;
}

export interface SeriesSkippedOccurrence {
  eventId: number;
  occurrenceIndex?: number | null;
  reason: string;
  details: string[];
}

/** Bulk operations report partial success rather than failing outright. */
export interface SeriesBulkResult {
  seriesId: number;
  affectedCount: number;
  affectedEventIds: number[];
  skipped: SeriesSkippedOccurrence[];
  retimedWithRegistrations: number[];
}

export interface UpdateFutureOccurrencesPayload {
  fromEventId: number;
  includeOverridden?: boolean;
  name?: string;
  description?: string;
  location?: string;
  venueName?: string;
  city?: string;
  latitude?: number | null;
  longitude?: number | null;
  maxParticipants?: number;
  registerCost?: number;
  isPrivate?: boolean;
  waitlistEnabled?: boolean;
  category?: EventCategory;
  tags?: string[];
  imageUrls?: string[];
  /** Wall-clock time of day (`HH:mm`) in the series' zone. */
  localStartTime?: string;
  durationMinutes?: number;
}
