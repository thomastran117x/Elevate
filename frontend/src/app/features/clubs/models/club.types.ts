import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';

export type ClubType = 'Sports' | 'Academic' | 'Social' | 'Cultural' | 'Gaming' | 'Other';

export const ALL_CLUB_TYPES: ClubType[] = [
  'Sports',
  'Academic',
  'Social',
  'Cultural',
  'Gaming',
  'Other',
];

export const ALL_CLUB_SORTS = ['Relevance', 'Newest', 'Members', 'Rating'] as const;
export type ClubSortBy = (typeof ALL_CLUB_SORTS)[number];

export const CLUB_TYPE_STYLES: Record<ClubType, string> = {
  Sports: 'bg-green-100 text-green-800',
  Academic: 'bg-blue-100 text-blue-800',
  Social: 'bg-yellow-100 text-yellow-800',
  Cultural: 'bg-purple-100 text-purple-800',
  Gaming: 'bg-red-100 text-red-800',
  Other: 'bg-gray-100 text-gray-800',
};

export interface Club {
  id: number;
  ownerId: number;
  name: string;
  description: string;
  clubType: ClubType;
  clubImage: string;
  memberCount: number;
  eventCount: number;
  availableEventCount: number;
  maxMemberCount: number;
  isPrivate: boolean;
  rating: number | null;
  location: string | null;
  phone: string | null;
  email: string | null;
  websiteUrl: string | null;
}

export interface ClubsPagedData {
  items: Club[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type ClubsApiResponse = ApiEnvelope<ClubsPagedData>;
export type ClubApiResponse = ApiEnvelope<Club>;

// Raw payload types to handle PascalCase from backend
type ClubPayload = Partial<Club> & {
  Id?: number;
  OwnerId?: number;
  Name?: string;
  Description?: string;
  Clubtype?: string;
  clubtype?: string;
  ClubImage?: string;
  MemberCount?: number;
  EventCount?: number;
  AvaliableEventCount?: number;
  availableEventCount?: number;
  MaxMemberCount?: number;
  IsPrivate?: boolean;
  Rating?: number | null;
  Location?: string | null;
  Phone?: string | null;
  Email?: string | null;
  WebsiteUrl?: string | null;
};

type PagedPayload<T> = {
  items?: T[];
  Items?: T[];
  totalCount?: number;
  TotalCount?: number;
  page?: number;
  Page?: number;
  pageSize?: number;
  PageSize?: number;
  totalPages?: number;
  TotalPages?: number;
};

const CLUB_TYPES: ClubType[] = ['Sports', 'Academic', 'Social', 'Cultural', 'Gaming', 'Other'];

function normalizeClubType(value: string | undefined): ClubType {
  if (!value) return 'Other';
  return CLUB_TYPES.includes(value as ClubType) ? (value as ClubType) : 'Other';
}

export function normalizeClub(raw: ClubPayload): Club {
  return {
    id: raw.id ?? raw.Id ?? 0,
    ownerId: raw.ownerId ?? raw.OwnerId ?? 0,
    name: raw.name ?? raw.Name ?? '',
    description: raw.description ?? raw.Description ?? '',
    clubType: normalizeClubType(raw.clubType ?? raw.Clubtype ?? raw.clubtype),
    clubImage: raw.clubImage ?? raw.ClubImage ?? '',
    memberCount: raw.memberCount ?? raw.MemberCount ?? 0,
    eventCount: raw.eventCount ?? raw.EventCount ?? 0,
    availableEventCount: raw.availableEventCount ?? raw.AvaliableEventCount ?? 0,
    maxMemberCount: raw.maxMemberCount ?? raw.MaxMemberCount ?? 0,
    isPrivate: raw.isPrivate ?? raw.IsPrivate ?? false,
    rating: raw.rating ?? raw.Rating ?? null,
    location: raw.location ?? raw.Location ?? null,
    phone: raw.phone ?? raw.Phone ?? null,
    email: raw.email ?? raw.Email ?? null,
    websiteUrl: raw.websiteUrl ?? raw.WebsiteUrl ?? null,
  };
}

export function normalizeClubsPagedData(raw: PagedPayload<ClubPayload>): ClubsPagedData {
  return {
    items: (raw.items ?? raw.Items ?? []).map(normalizeClub),
    totalCount: raw.totalCount ?? raw.TotalCount ?? 0,
    page: raw.page ?? raw.Page ?? 1,
    pageSize: raw.pageSize ?? raw.PageSize ?? 20,
    totalPages: raw.totalPages ?? raw.TotalPages ?? 0,
  };
}
