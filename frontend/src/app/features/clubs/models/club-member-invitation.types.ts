export interface ClubMemberInvitationClubSummary {
  id: number;
  name: string;
  clubImage: string;
}

/** A pending club member invitation, as shown to organisers in the members tab. */
export interface ClubMemberInvitation {
  clubId: number;
  recipientUserId: number;
  recipientEmail: string;
  createdAtUtc: string;
  expiresAtUtc: string;
}

/** A shareable club member invite link. */
export interface ClubInvitationLink {
  id: number;
  clubId: number;
  shareUrl?: string | null;
  expiresAt: string;
  maxRedemptions?: number | null;
  redemptionCount: number;
  isRevoked: boolean;
  revokedAtUtc?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ClubMemberInvitationResolve {
  state: string;
  source: string;
  requiresAuthentication: boolean;
  canAccept: boolean;
  canDecline: boolean;
  expiresAtUtc?: string | null;
  club?: ClubMemberInvitationClubSummary | null;
}

export interface ClubMemberInvitationDecision {
  clubId: number;
  accepted: boolean;
}

// ---------------------------------------------------------------------------
// Normalizers (tolerate both camelCase and PascalCase envelopes)
// ---------------------------------------------------------------------------

type ClubMemberInvitationPayload = Partial<ClubMemberInvitation> & {
  ClubId?: number;
  RecipientUserId?: number;
  RecipientEmail?: string;
  CreatedAtUtc?: string;
  ExpiresAtUtc?: string;
};

type ClubSummaryPayload = Partial<ClubMemberInvitationClubSummary> & {
  Id?: number;
  Name?: string;
  ClubImage?: string;
};

type ClubInvitationLinkPayload = Partial<ClubInvitationLink> & {
  Id?: number;
  ClubId?: number;
  ShareUrl?: string | null;
  ExpiresAt?: string;
  MaxRedemptions?: number | null;
  RedemptionCount?: number;
  IsRevoked?: boolean;
  RevokedAtUtc?: string | null;
  CreatedAt?: string;
  UpdatedAt?: string;
};

type ClubMemberInvitationResolvePayload = Partial<ClubMemberInvitationResolve> & {
  State?: string;
  Source?: string;
  RequiresAuthentication?: boolean;
  CanAccept?: boolean;
  CanDecline?: boolean;
  ExpiresAtUtc?: string | null;
  Club?: ClubSummaryPayload | null;
};

type ClubMemberInvitationDecisionPayload = Partial<ClubMemberInvitationDecision> & {
  ClubId?: number;
  Accepted?: boolean;
};

export function normalizeClubMemberInvitation(
  raw: ClubMemberInvitationPayload,
): ClubMemberInvitation {
  return {
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    recipientUserId: raw.recipientUserId ?? raw.RecipientUserId ?? 0,
    recipientEmail: raw.recipientEmail ?? raw.RecipientEmail ?? '',
    createdAtUtc: raw.createdAtUtc ?? raw.CreatedAtUtc ?? '',
    expiresAtUtc: raw.expiresAtUtc ?? raw.ExpiresAtUtc ?? '',
  };
}

export function normalizeClubInvitationLink(raw: ClubInvitationLinkPayload): ClubInvitationLink {
  return {
    id: raw.id ?? raw.Id ?? 0,
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    shareUrl: raw.shareUrl ?? raw.ShareUrl ?? null,
    expiresAt: raw.expiresAt ?? raw.ExpiresAt ?? '',
    maxRedemptions: raw.maxRedemptions ?? raw.MaxRedemptions ?? null,
    redemptionCount: raw.redemptionCount ?? raw.RedemptionCount ?? 0,
    isRevoked: raw.isRevoked ?? raw.IsRevoked ?? false,
    revokedAtUtc: raw.revokedAtUtc ?? raw.RevokedAtUtc ?? null,
    createdAt: raw.createdAt ?? raw.CreatedAt ?? '',
    updatedAt: raw.updatedAt ?? raw.UpdatedAt ?? '',
  };
}

function normalizeClubSummary(raw: ClubSummaryPayload): ClubMemberInvitationClubSummary {
  return {
    id: raw.id ?? raw.Id ?? 0,
    name: raw.name ?? raw.Name ?? '',
    clubImage: raw.clubImage ?? raw.ClubImage ?? '',
  };
}

export function normalizeClubMemberInvitationResolve(
  raw: ClubMemberInvitationResolvePayload,
): ClubMemberInvitationResolve {
  const club = raw.club ?? raw.Club;
  return {
    state: raw.state ?? raw.State ?? '',
    source: raw.source ?? raw.Source ?? '',
    requiresAuthentication: raw.requiresAuthentication ?? raw.RequiresAuthentication ?? false,
    canAccept: raw.canAccept ?? raw.CanAccept ?? false,
    canDecline: raw.canDecline ?? raw.CanDecline ?? false,
    expiresAtUtc: raw.expiresAtUtc ?? raw.ExpiresAtUtc ?? null,
    club: club ? normalizeClubSummary(club) : null,
  };
}

export function normalizeClubMemberInvitationDecision(
  raw: ClubMemberInvitationDecisionPayload,
): ClubMemberInvitationDecision {
  return {
    clubId: raw.clubId ?? raw.ClubId ?? 0,
    accepted: raw.accepted ?? raw.Accepted ?? false,
  };
}
