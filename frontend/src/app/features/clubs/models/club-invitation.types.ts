import { ClubStaffRole } from './club-management.types';

export interface ClubInvitationClubSummary {
  id: number;
  name: string;
  clubImage: string;
}

/** A pending club staff invitation, as shown to the owner in the staff tab. */
export interface ClubInvitation {
  clubId: number;
  recipientUserId: number;
  recipientEmail: string;
  role: ClubStaffRole;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface ClubInvitationResolve {
  state: string;
  requiresAuthentication: boolean;
  canAccept: boolean;
  canDecline: boolean;
  role?: ClubStaffRole | null;
  expiresAtUtc?: string | null;
  club?: ClubInvitationClubSummary | null;
}

export interface ClubInvitationDecision {
  clubId: number;
  role: ClubStaffRole;
  accepted: boolean;
}

// ---------------------------------------------------------------------------
// Normalizers (tolerate both camelCase and PascalCase envelopes)
// ---------------------------------------------------------------------------

function normalizeRole(value: unknown): ClubStaffRole {
  return value === 'Volunteer' ? 'Volunteer' : 'Manager';
}

type ClubInvitationPayload = Partial<ClubInvitation> & {
  ClubId?: number;
  RecipientUserId?: number;
  RecipientEmail?: string;
  Role?: string;
  CreatedAtUtc?: string;
  ExpiresAtUtc?: string;
};

type ClubSummaryPayload = Partial<ClubInvitationClubSummary> & {
  Id?: number;
  Name?: string;
  ClubImage?: string;
};

type ClubInvitationResolvePayload = Partial<ClubInvitationResolve> & {
  State?: string;
  RequiresAuthentication?: boolean;
  CanAccept?: boolean;
  CanDecline?: boolean;
  Role?: string | null;
  ExpiresAtUtc?: string | null;
  Club?: ClubSummaryPayload | null;
};

type ClubInvitationDecisionPayload = Partial<ClubInvitationDecision> & {
  ClubId?: number;
  Role?: string;
  Accepted?: boolean;
};

export function normalizeClubInvitation(raw: ClubInvitationPayload): ClubInvitation {
  return {
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    recipientUserId: raw.recipientUserId ?? raw.RecipientUserId ?? 0,
    recipientEmail: raw.recipientEmail ?? raw.RecipientEmail ?? '',
    role: normalizeRole(raw.role ?? raw.Role),
    createdAtUtc: raw.createdAtUtc ?? raw.CreatedAtUtc ?? '',
    expiresAtUtc: raw.expiresAtUtc ?? raw.ExpiresAtUtc ?? '',
  };
}

function normalizeClubSummary(raw: ClubSummaryPayload): ClubInvitationClubSummary {
  return {
    id: raw.id ?? raw.Id ?? 0,
    name: raw.name ?? raw.Name ?? '',
    clubImage: raw.clubImage ?? raw.ClubImage ?? '',
  };
}

export function normalizeClubInvitationResolve(
  raw: ClubInvitationResolvePayload,
): ClubInvitationResolve {
  const club = raw.club ?? raw.Club;
  const role = raw.role ?? raw.Role;
  return {
    state: raw.state ?? raw.State ?? '',
    requiresAuthentication: raw.requiresAuthentication ?? raw.RequiresAuthentication ?? false,
    canAccept: raw.canAccept ?? raw.CanAccept ?? false,
    canDecline: raw.canDecline ?? raw.CanDecline ?? false,
    role: role ? normalizeRole(role) : null,
    expiresAtUtc: raw.expiresAtUtc ?? raw.ExpiresAtUtc ?? null,
    club: club ? normalizeClubSummary(club) : null,
  };
}

export function normalizeClubInvitationDecision(
  raw: ClubInvitationDecisionPayload,
): ClubInvitationDecision {
  return {
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    role: normalizeRole(raw.role ?? raw.Role),
    accepted: raw.accepted ?? raw.Accepted ?? false,
  };
}
