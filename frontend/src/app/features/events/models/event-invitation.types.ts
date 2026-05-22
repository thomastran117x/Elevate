import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';

export interface EventInvitationSummaryEvent {
  id: number;
  name: string;
  description: string;
  location: string;
  isPrivate: boolean;
  registerCost: number;
  maxParticipants: number;
  registrationCount: number;
  startTime: string;
  endTime?: string | null;
  status: string;
  category: string;
  imageUrls: string[];
  club?: {
    id: number;
    name: string;
    description: string;
    clubType: string;
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
  };
}

export interface EventInvitation {
  id: number;
  eventId: number;
  recipientUserId?: number | null;
  recipientEmail?: string | null;
  sourceType: string;
  lifecycleStatus: string;
  effectiveStatus: string;
  deliveryStatus: string;
  expiresAt?: string | null;
  acceptedAtUtc?: string | null;
  declinedAtUtc?: string | null;
  revokedAtUtc?: string | null;
  eventInvitationLinkId?: number | null;
  deliveryError?: string | null;
  createdAt: string;
  updatedAt: string;
  event?: EventInvitationSummaryEvent;
}

export interface EventInvitationLink {
  id: number;
  eventId: number;
  shareUrl?: string | null;
  expiresAt: string;
  maxRedemptions: number;
  redemptionCount: number;
  isRevoked: boolean;
  revokedAtUtc?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface EventInvitationResolve {
  state: string;
  requiresAuthentication: boolean;
  canAccept: boolean;
  canDecline: boolean;
  sourceType: string;
  expiresAt?: string | null;
  event?: EventInvitationSummaryEvent;
}

export interface EventInvitationDecision {
  invitation: EventInvitation;
}

export interface CreateEventInvitationsPayload {
  userIds?: number[];
  emails?: string[];
  expiresAt?: string | null;
}

export interface CreateEventInvitationLinkPayload {
  maxRedemptions: number;
  expiresAt: string;
}

export type EventInvitationListResponse = ApiEnvelope<EventInvitation[]>;
export type EventInvitationLinkListResponse = ApiEnvelope<EventInvitationLink[]>;
export type EventInvitationResolveResponse = ApiEnvelope<EventInvitationResolve>;
export type EventInvitationDecisionResponse = ApiEnvelope<EventInvitationDecision>;
