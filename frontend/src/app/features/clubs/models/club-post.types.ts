import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';

export type PostType = 'General' | 'Announcement' | 'Event' | 'Poll';

export const POST_TYPE_LABELS: Record<PostType, string> = {
  General: 'General',
  Announcement: 'Announcement',
  Event: 'Event',
  Poll: 'Poll',
};

export const POST_TYPE_STYLES: Record<PostType, string> = {
  General: 'bg-white/10 text-white/70 border-white/15',
  Announcement: 'bg-amber-500/20 text-amber-300 border-amber-500/30',
  Event: 'bg-purple-500/20 text-purple-300 border-purple-500/30',
  Poll: 'bg-cyan-500/20 text-cyan-300 border-cyan-500/30',
};

export const ALL_POST_SORTS = ['Recent', 'Popular'] as const;
export type PostSortBy = (typeof ALL_POST_SORTS)[number];

export interface AuthorInfo {
  id: number;
  name: string | null;
  username: string | null;
  avatar: string | null;
}

export interface ClubPost {
  id: number;
  clubId: number;
  userId: number;
  title: string;
  content: string;
  postType: PostType;
  likesCount: number;
  viewCount: number;
  isPinned: boolean;
  author: AuthorInfo | null;
  createdAt: string;
  updatedAt: string;
}

export interface ClubPostsPagedData {
  items: ClubPost[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type ClubPostsApiResponse = ApiEnvelope<ClubPostsPagedData>;

export interface PostComment {
  id: number;
  postId: number;
  userId: number;
  content: string;
  author: AuthorInfo | null;
  createdAt: string;
  updatedAt: string;
}

export interface PostCommentsPagedData {
  items: PostComment[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type PostCommentsApiResponse = ApiEnvelope<PostCommentsPagedData>;
export type PostCommentApiResponse = ApiEnvelope<PostComment>;

// Raw payload types to handle PascalCase from backend
type AuthorInfoPayload = Partial<AuthorInfo> & {
  Id?: number;
  Name?: string | null;
  Username?: string | null;
  Avatar?: string | null;
};

type ClubPostPayload = Partial<ClubPost> & {
  Id?: number;
  ClubId?: number;
  UserId?: number;
  Title?: string;
  Content?: string;
  PostType?: string | number;
  LikesCount?: number;
  ViewCount?: number;
  IsPinned?: boolean;
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

type PostCommentPayload = Partial<PostComment> & {
  Id?: number;
  PostId?: number;
  UserId?: number;
  Content?: string;
  Author?: AuthorInfoPayload | null;
  CreatedAt?: string;
  UpdatedAt?: string;
};

const POST_TYPES: PostType[] = ['General', 'Announcement', 'Event', 'Poll'];

export function normalizeAuthor(raw: AuthorInfoPayload | null | undefined): AuthorInfo | null {
  if (!raw) return null;
  return {
    id: raw.id ?? raw.Id ?? 0,
    name: raw.name ?? raw.Name ?? null,
    username: raw.username ?? raw.Username ?? null,
    avatar: raw.avatar ?? raw.Avatar ?? null,
  };
}

export function normalizePostType(value: string | number | undefined): PostType {
  if (typeof value === 'number') return POST_TYPES[value] ?? 'General';
  return POST_TYPES.includes(value as PostType) ? (value as PostType) : 'General';
}

export function normalizeClubPost(raw: ClubPostPayload): ClubPost {
  return {
    id: raw.id ?? raw.Id ?? 0,
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    userId: raw.userId ?? raw.UserId ?? 0,
    title: raw.title ?? raw.Title ?? '',
    content: raw.content ?? raw.Content ?? '',
    postType: normalizePostType(raw.postType ?? raw.PostType),
    likesCount: raw.likesCount ?? raw.LikesCount ?? 0,
    viewCount: raw.viewCount ?? raw.ViewCount ?? 0,
    isPinned: raw.isPinned ?? raw.IsPinned ?? false,
    author: normalizeAuthor(raw.author ?? raw.Author),
    createdAt: raw.createdAt ?? raw.CreatedAt ?? '',
    updatedAt: raw.updatedAt ?? raw.UpdatedAt ?? '',
  };
}

export function normalizePostComment(raw: PostCommentPayload): PostComment {
  return {
    id: raw.id ?? raw.Id ?? 0,
    postId: raw.postId ?? raw.PostId ?? 0,
    userId: raw.userId ?? raw.UserId ?? 0,
    content: raw.content ?? raw.Content ?? '',
    author: normalizeAuthor(raw.author ?? raw.Author),
    createdAt: raw.createdAt ?? raw.CreatedAt ?? '',
    updatedAt: raw.updatedAt ?? raw.UpdatedAt ?? '',
  };
}

export function normalizeClubPostsPagedData(
  raw: PagedPayload<ClubPostPayload>,
): ClubPostsPagedData {
  return {
    items: (raw.items ?? raw.Items ?? []).map(normalizeClubPost),
    totalCount: raw.totalCount ?? raw.TotalCount ?? 0,
    page: raw.page ?? raw.Page ?? 1,
    pageSize: raw.pageSize ?? raw.PageSize ?? 20,
    totalPages: raw.totalPages ?? raw.TotalPages ?? 0,
  };
}

export function normalizePostCommentsPagedData(
  raw: PagedPayload<PostCommentPayload>,
): PostCommentsPagedData {
  return {
    items: (raw.items ?? raw.Items ?? []).map(normalizePostComment),
    totalCount: raw.totalCount ?? raw.TotalCount ?? 0,
    page: raw.page ?? raw.Page ?? 1,
    pageSize: raw.pageSize ?? raw.PageSize ?? 20,
    totalPages: raw.totalPages ?? raw.TotalPages ?? 0,
  };
}
