import {
  ALL_CATEGORIES,
  ALL_LIFECYCLE_STATES,
  ALL_STATUSES,
  ClubType,
  EventCategory,
  EventHostClub,
  EventItem,
  EventLifecycleState,
  EventStatus,
} from './event.types';

/**
 * Wire shape of an event: the API serializes property names in camelCase but enums as integers,
 * and some endpoints have historically returned PascalCase, so both spellings are tolerated.
 */
export type EventItemPayload = EventItem & {
  Id?: number;
  Name?: string;
  Description?: string;
  Location?: string;
  ImageUrls?: string[];
  IsPrivate?: boolean;
  MaxParticipants?: number;
  RegisterCost?: number;
  StartTime?: string;
  EndTime?: string;
  ClubId?: number;
  CreatedAt?: string;
  LifecycleState?: string | number;
  Status?: string | number;
  Category?: string | number;
  VenueName?: string;
  City?: string;
  Latitude?: number;
  Longitude?: number;
  Tags?: string[];
  RegistrationCount?: number;
  WaitlistEnabled?: boolean;
  WaitlistCount?: number;
  DistanceKm?: number;
  Club?: EventHostClubPayload;
};

export type EventHostClubPayload = EventHostClub & {
  Id?: number;
  Name?: string;
  Description?: string;
  ClubType?: string | number;
  Clubtype?: string | number;
  ClubImage?: string;
  MemberCount?: number;
  EventCount?: number;
  AvailableEventCount?: number;
  AvaliableEventCount?: number;
  IsPrivate?: boolean;
  Email?: string;
  Phone?: string;
  Rating?: number;
  WebsiteUrl?: string;
  Location?: string;
};

/**
 * Turns an event payload into the domain shape, decoding the integer enums.
 *
 * Shared rather than reimplemented per service on purpose. Every endpoint that returns an event
 * nested inside something else — waitlist entries, pinned events — needs this, and casting the
 * payload straight to `EventItem` instead silently leaves `lifecycleState` as a number, so every
 * `=== 'Paused'` or `=== 'Cancelled'` comparison downstream is quietly always false.
 */
export function normalizeEventItem(item: EventItemPayload | null | undefined): EventItem {
  // Tolerates a missing payload rather than throwing: several endpoints embed an event
  // optionally, and `EventItem` is non-optional on the domain types, so a fully defaulted
  // event is the honest answer. Callers used to cast, which quietly produced `undefined` here.
  item ??= {} as EventItemPayload;

  return {
    id: item.id ?? item.Id ?? 0,
    name: item.name ?? item.Name ?? '',
    description: item.description ?? item.Description ?? '',
    location: item.location ?? item.Location ?? '',
    imageUrls: item.imageUrls ?? item.ImageUrls ?? [],
    isPrivate: item.isPrivate ?? item.IsPrivate ?? false,
    maxParticipants: item.maxParticipants ?? item.MaxParticipants ?? 0,
    registerCost: item.registerCost ?? item.RegisterCost ?? 0,
    startTime: item.startTime ?? item.StartTime ?? '',
    endTime: item.endTime ?? item.EndTime,
    clubId: item.clubId ?? item.ClubId ?? 0,
    createdAt: item.createdAt ?? item.CreatedAt ?? '',
    lifecycleState: normalizeLifecycleState(item.lifecycleState ?? item.LifecycleState),
    status: normalizeStatus(item.status ?? item.Status),
    category: normalizeCategory(item.category ?? item.Category),
    venueName: item.venueName ?? item.VenueName,
    city: item.city ?? item.City,
    latitude: item.latitude ?? item.Latitude,
    longitude: item.longitude ?? item.Longitude,
    tags: item.tags ?? item.Tags ?? [],
    registrationCount: item.registrationCount ?? item.RegistrationCount ?? 0,
    waitlistEnabled: item.waitlistEnabled ?? item.WaitlistEnabled ?? false,
    waitlistCount: item.waitlistCount ?? item.WaitlistCount ?? 0,
    distanceKm: item.distanceKm ?? item.DistanceKm,
    club: normalizeEventHostClub(item.club ?? item.Club),
  };
}

export function normalizeEventHostClub(
  value: EventHostClubPayload | undefined,
): EventHostClub | undefined {
  if (!value) {
    return undefined;
  }

  return {
    id: value.id ?? value.Id ?? 0,
    name: value.name ?? value.Name ?? '',
    description: value.description ?? value.Description ?? '',
    clubType: normalizeClubType(value.clubType ?? value.ClubType ?? value.Clubtype),
    clubImage: value.clubImage ?? value.ClubImage ?? '',
    memberCount: value.memberCount ?? value.MemberCount ?? 0,
    eventCount: value.eventCount ?? value.EventCount ?? 0,
    availableEventCount:
      value.availableEventCount ?? value.AvailableEventCount ?? value.AvaliableEventCount ?? 0,
    isPrivate: value.isPrivate ?? value.IsPrivate ?? false,
    email: value.email ?? value.Email,
    phone: value.phone ?? value.Phone,
    rating: value.rating ?? value.Rating,
    websiteUrl: value.websiteUrl ?? value.WebsiteUrl,
    location: value.location ?? value.Location,
  };
}

/**
 * Decodes a lifecycle state, which crosses the wire as an integer index into
 * `ALL_LIFECYCLE_STATES`. Falls back to `Published` for anything unrecognized, matching the
 * long-standing behaviour of the public read path.
 */
export function normalizeLifecycleState(value: string | number | undefined): EventLifecycleState {
  if (typeof value === 'number') {
    return ALL_LIFECYCLE_STATES[value] ?? 'Published';
  }

  return ALL_LIFECYCLE_STATES.includes(value as EventLifecycleState)
    ? (value as EventLifecycleState)
    : 'Published';
}

export function normalizeStatus(value: string | number | undefined): EventStatus {
  if (typeof value === 'number') {
    return ALL_STATUSES[value] ?? 'Upcoming';
  }

  return ALL_STATUSES.includes(value as EventStatus) ? (value as EventStatus) : 'Upcoming';
}

export function normalizeCategory(value: string | number | undefined): EventCategory {
  if (typeof value === 'number') {
    return ALL_CATEGORIES[value] ?? 'Other';
  }

  return ALL_CATEGORIES.includes(value as EventCategory) ? (value as EventCategory) : 'Other';
}

export function normalizeClubType(value: string | number | undefined): ClubType {
  const clubTypes: ClubType[] = ['Sports', 'Academic', 'Social', 'Cultural', 'Gaming', 'Other'];

  if (typeof value === 'number') {
    return clubTypes[value] ?? 'Other';
  }

  return clubTypes.includes(value as ClubType) ? (value as ClubType) : 'Other';
}
