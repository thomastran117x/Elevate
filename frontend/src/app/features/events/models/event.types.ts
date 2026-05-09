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

export type EventSortBy = 'Relevance' | 'Date' | 'Distance' | 'Popularity';

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
  status: EventStatus;
  category: EventCategory;
  venueName?: string;
  city?: string;
  latitude?: number;
  longitude?: number;
  tags: string[];
  registrationCount: number;
  distanceKm?: number;
}

export interface EventsPagedData {
  items: EventItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface EventsResponseMeta {
  source?: string;
}

export type EventsApiResponse = ApiEnvelope<EventsPagedData, EventsResponseMeta>;

export interface EventSearchParams {
  search?: string;
  city?: string;
  category?: EventCategory;
  status?: EventStatus;
  sortBy?: EventSortBy;
  isPrivate?: boolean;
  tags?: string;
  page?: number;
  pageSize?: number;
}

export const ALL_CATEGORIES: EventCategory[] = [
  'Sports', 'Music', 'Academic', 'Workshop', 'Conference',
  'Social', 'Cultural', 'Gaming', 'Food', 'Fitness',
  'Networking', 'Volunteer', 'Party', 'Arts', 'Other',
];

export const CATEGORY_STYLES: Record<EventCategory, { badge: string; bg: string }> = {
  Sports:      { badge: 'bg-blue-500/20 text-blue-300 border-blue-500/30',      bg: 'bg-blue-500' },
  Music:       { badge: 'bg-fuchsia-500/20 text-fuchsia-300 border-fuchsia-500/30', bg: 'bg-fuchsia-500' },
  Academic:    { badge: 'bg-indigo-500/20 text-indigo-300 border-indigo-500/30', bg: 'bg-indigo-500' },
  Workshop:    { badge: 'bg-cyan-500/20 text-cyan-300 border-cyan-500/30',       bg: 'bg-cyan-500' },
  Conference:  { badge: 'bg-violet-500/20 text-violet-300 border-violet-500/30', bg: 'bg-violet-500' },
  Social:      { badge: 'bg-pink-500/20 text-pink-300 border-pink-500/30',       bg: 'bg-pink-500' },
  Cultural:    { badge: 'bg-orange-500/20 text-orange-300 border-orange-500/30', bg: 'bg-orange-500' },
  Gaming:      { badge: 'bg-green-500/20 text-green-300 border-green-500/30',    bg: 'bg-green-500' },
  Food:        { badge: 'bg-amber-500/20 text-amber-300 border-amber-500/30',    bg: 'bg-amber-500' },
  Fitness:     { badge: 'bg-red-500/20 text-red-300 border-red-500/30',          bg: 'bg-red-500' },
  Networking:  { badge: 'bg-teal-500/20 text-teal-300 border-teal-500/30',       bg: 'bg-teal-500' },
  Volunteer:   { badge: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30', bg: 'bg-emerald-500' },
  Party:       { badge: 'bg-rose-500/20 text-rose-300 border-rose-500/30',       bg: 'bg-rose-500' },
  Arts:        { badge: 'bg-yellow-500/20 text-yellow-300 border-yellow-500/30', bg: 'bg-yellow-500' },
  Other:       { badge: 'bg-slate-500/20 text-slate-300 border-slate-500/30',    bg: 'bg-slate-500' },
};
