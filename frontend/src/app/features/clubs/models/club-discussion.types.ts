import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { AuthorInfo, normalizeAuthor } from './club-post.types';

export interface ClubDiscussion {
  id: number;
  clubId: number;
  userId: number;
  title: string;
  description: string;
  author: AuthorInfo | null;
  createdAt: string;
  updatedAt: string;
}

export interface ClubDiscussionsPagedData {
  items: ClubDiscussion[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type ClubDiscussionsApiResponse = ApiEnvelope<ClubDiscussionsPagedData>;
export type ClubDiscussionApiResponse = ApiEnvelope<ClubDiscussion>;

// Raw payload types to handle PascalCase from backend
type AuthorInfoPayload = Partial<AuthorInfo> & {
  Id?: number;
  Name?: string | null;
  Username?: string | null;
  Avatar?: string | null;
};

type ClubDiscussionPayload = Partial<ClubDiscussion> & {
  Id?: number;
  ClubId?: number;
  UserId?: number;
  Title?: string;
  Description?: string;
  Author?: AuthorInfoPayload | null;
  CreatedAt?: string;
  UpdatedAt?: string;
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

export function normalizeClubDiscussion(raw: ClubDiscussionPayload): ClubDiscussion {
  return {
    id: raw.id ?? raw.Id ?? 0,
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    userId: raw.userId ?? raw.UserId ?? 0,
    title: raw.title ?? raw.Title ?? '',
    description: raw.description ?? raw.Description ?? '',
    author: normalizeAuthor(raw.author ?? raw.Author),
    createdAt: raw.createdAt ?? raw.CreatedAt ?? '',
    updatedAt: raw.updatedAt ?? raw.UpdatedAt ?? '',
  };
}

export function normalizeClubDiscussionsPagedData(
  raw: PagedPayload<ClubDiscussionPayload>,
): ClubDiscussionsPagedData {
  return {
    items: (raw.items ?? raw.Items ?? []).map(normalizeClubDiscussion),
    totalCount: raw.totalCount ?? raw.TotalCount ?? 0,
    page: raw.page ?? raw.Page ?? 1,
    pageSize: raw.pageSize ?? raw.PageSize ?? 20,
    totalPages: raw.totalPages ?? raw.TotalPages ?? 0,
  };
}

/** Display name for a discussion's author, falling back through name → username → user id. */
export function discussionAuthorName(discussion: ClubDiscussion): string {
  return discussion.author?.name ?? discussion.author?.username ?? `User #${discussion.userId}`;
}
