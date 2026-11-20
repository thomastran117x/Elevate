import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { AuthorInfo, normalizeAuthor } from './club-post.types';

export type ClubModerationAction = 'remove' | 'edit' | 'pin' | 'unpin';

/** One action a moderator took on someone else's content, as recorded in the club's log. */
export interface ClubModerationLogItem {
  id: number;
  clubId: number;
  action: ClubModerationAction;
  /** `Post`, `Discussion`, `Reply`, `Comment` or `Review`. */
  contentType: string;
  contentId: number;
  /** The title or opening text as it was before the action. */
  contentSnapshot: string | null;
  reason: string | null;
  /** The actor's standing when they acted: `Owner`, `Manager`, `Volunteer` or `Admin`. */
  actorClubRole: string;
  actor: AuthorInfo;
  author: AuthorInfo;
  createdAt: string;
}

export interface ClubModerationLogPage {
  items: ClubModerationLogItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type ClubModerationLogApiResponse = ApiEnvelope<ClubModerationLogPage>;

type AuthorPayload = Parameters<typeof normalizeAuthor>[0];

export type ClubModerationLogItemPayload = Partial<
  Omit<ClubModerationLogItem, 'actor' | 'author' | 'action'>
> & {
  action?: string;
  actor?: AuthorPayload;
  author?: AuthorPayload;
  Id?: number;
  ClubId?: number;
  Action?: string;
  ContentType?: string;
  ContentId?: number;
  ContentSnapshot?: string | null;
  Reason?: string | null;
  ActorClubRole?: string;
  Actor?: AuthorPayload;
  Author?: AuthorPayload;
  CreatedAt?: string;
};

export interface ClubModerationLogPagePayload {
  items?: ClubModerationLogItemPayload[];
  Items?: ClubModerationLogItemPayload[];
  totalCount?: number;
  TotalCount?: number;
  page?: number;
  Page?: number;
  pageSize?: number;
  PageSize?: number;
  totalPages?: number;
  TotalPages?: number;
}

const ACTIONS: readonly ClubModerationAction[] = ['remove', 'edit', 'pin', 'unpin'];

function normalizeAction(value: string | undefined): ClubModerationAction {
  const lowered = (value ?? '').toLowerCase();
  return (ACTIONS as readonly string[]).includes(lowered)
    ? (lowered as ClubModerationAction)
    : 'remove';
}

/** A missing user still renders, by id, rather than dropping the entry. */
function normalizeParty(raw: AuthorPayload): AuthorInfo {
  return (
    normalizeAuthor(raw) ?? {
      id: 0,
      name: null,
      username: null,
      usernameDisplay: null,
      avatar: null,
    }
  );
}

export function normalizeClubModerationLogItem(
  raw: ClubModerationLogItemPayload,
): ClubModerationLogItem {
  return {
    id: raw.id ?? raw.Id ?? 0,
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    action: normalizeAction(raw.action ?? raw.Action),
    contentType: raw.contentType ?? raw.ContentType ?? '',
    contentId: raw.contentId ?? raw.ContentId ?? 0,
    contentSnapshot: raw.contentSnapshot ?? raw.ContentSnapshot ?? null,
    reason: raw.reason ?? raw.Reason ?? null,
    actorClubRole: raw.actorClubRole ?? raw.ActorClubRole ?? '',
    actor: normalizeParty(raw.actor ?? raw.Actor),
    author: normalizeParty(raw.author ?? raw.Author),
    createdAt: raw.createdAt ?? raw.CreatedAt ?? '',
  };
}

export function normalizeClubModerationLogPage(
  raw: ClubModerationLogPagePayload,
): ClubModerationLogPage {
  return {
    items: (raw.items ?? raw.Items ?? []).map(normalizeClubModerationLogItem),
    totalCount: raw.totalCount ?? raw.TotalCount ?? 0,
    page: raw.page ?? raw.Page ?? 1,
    pageSize: raw.pageSize ?? raw.PageSize ?? 20,
    totalPages: raw.totalPages ?? raw.TotalPages ?? 0,
  };
}
