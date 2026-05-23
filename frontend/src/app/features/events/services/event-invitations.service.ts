import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import {
  CreateEventInvitationLinkPayload,
  CreateEventInvitationsPayload,
  EventInvitation,
  EventInvitationDecision,
  EventInvitationLink,
  EventInvitationResolve,
  EventInvitationSummaryEvent,
} from '../models/event-invitation.types';

type InvitationPayload = EventInvitation & {
  Id?: number;
  EventId?: number;
  RecipientUserId?: number | null;
  RecipientEmail?: string | null;
  SourceType?: string;
  LifecycleStatus?: string;
  EffectiveStatus?: string;
  DeliveryStatus?: string;
  ExpiresAt?: string | null;
  AcceptedAtUtc?: string | null;
  DeclinedAtUtc?: string | null;
  RevokedAtUtc?: string | null;
  EventInvitationLinkId?: number | null;
  DeliveryError?: string | null;
  CreatedAt?: string;
  UpdatedAt?: string;
  Event?: SummaryEventPayload;
};

type InvitationLinkPayload = EventInvitationLink & {
  Id?: number;
  EventId?: number;
  ShareUrl?: string | null;
  ExpiresAt?: string;
  MaxRedemptions?: number;
  RedemptionCount?: number;
  IsRevoked?: boolean;
  RevokedAtUtc?: string | null;
  CreatedAt?: string;
  UpdatedAt?: string;
};

type ResolvePayload = EventInvitationResolve & {
  State?: string;
  RequiresAuthentication?: boolean;
  CanAccept?: boolean;
  CanDecline?: boolean;
  SourceType?: string;
  ExpiresAt?: string | null;
  Event?: SummaryEventPayload;
};

type DecisionPayload = EventInvitationDecision & {
  Invitation?: InvitationPayload;
};

type SummaryEventPayload = EventInvitationSummaryEvent & {
  Id?: number;
  Name?: string;
  Description?: string;
  Location?: string;
  IsPrivate?: boolean;
  RegisterCost?: number;
  MaxParticipants?: number;
  RegistrationCount?: number;
  StartTime?: string;
  EndTime?: string | null;
  Status?: string;
  Category?: string;
  ImageUrls?: string[];
  Club?: EventInvitationSummaryEvent['club'];
};

@Injectable({ providedIn: 'root' })
export class EventInvitationsService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private http: HttpClient) {}

  resolve(token: string): Observable<EventInvitationResolve> {
    return this.http
      .post<
        ApiEnvelope<ResolvePayload>
      >(`${this.base}/invitations/resolve`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeResolve(
            requireEnvelopeData(response, 'Invitation response was incomplete.'),
          ),
        ),
      );
  }

  accept(token: string): Observable<EventInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<DecisionPayload>
      >(`${this.base}/invitations/accept`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.'),
          ),
        ),
      );
  }

  decline(token: string): Observable<EventInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<DecisionPayload>
      >(`${this.base}/invitations/decline`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.'),
          ),
        ),
      );
  }

  acceptById(invitationId: number): Observable<EventInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<DecisionPayload>
      >(`${this.base}/invitations/${invitationId}/accept`, {}, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.'),
          ),
        ),
      );
  }

  declineById(invitationId: number): Observable<EventInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<DecisionPayload>
      >(`${this.base}/invitations/${invitationId}/decline`, {}, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.'),
          ),
        ),
      );
  }

  getMine(): Observable<EventInvitation[]> {
    return this.http
      .get<ApiEnvelope<InvitationPayload[]>>(`${this.base}/me/invited`, { withCredentials: true })
      .pipe(
        map((response) =>
          (requireEnvelopeData(response, 'Invitations response was incomplete.') ?? []).map(
            (item) => this.normalizeInvitation(item),
          ),
        ),
      );
  }

  getEventInvitations(eventId: number): Observable<EventInvitation[]> {
    return this.http
      .get<
        ApiEnvelope<InvitationPayload[]>
      >(`${this.base}/${eventId}/invitations`, { withCredentials: true })
      .pipe(
        map((response) =>
          (requireEnvelopeData(response, 'Invitations response was incomplete.') ?? []).map(
            (item) => this.normalizeInvitation(item),
          ),
        ),
      );
  }

  createInvitations(
    eventId: number,
    payload: CreateEventInvitationsPayload,
  ): Observable<EventInvitation[]> {
    return this.http
      .post<
        ApiEnvelope<InvitationPayload[]>
      >(`${this.base}/${eventId}/invitations`, payload, { withCredentials: true })
      .pipe(
        map((response) =>
          (requireEnvelopeData(response, 'Invitations response was incomplete.') ?? []).map(
            (item) => this.normalizeInvitation(item),
          ),
        ),
      );
  }

  revokeInvitation(eventId: number, invitationId: number): Observable<EventInvitation> {
    return this.http
      .post<
        ApiEnvelope<InvitationPayload>
      >(`${this.base}/${eventId}/invitations/${invitationId}/revoke`, {}, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeInvitation(
            requireEnvelopeData(response, 'Invitation response was incomplete.'),
          ),
        ),
      );
  }

  getInvitationLinks(eventId: number): Observable<EventInvitationLink[]> {
    return this.http
      .get<
        ApiEnvelope<InvitationLinkPayload[]>
      >(`${this.base}/${eventId}/invitation-links`, { withCredentials: true })
      .pipe(
        map((response) =>
          (requireEnvelopeData(response, 'Invitation links response was incomplete.') ?? []).map(
            (item) => this.normalizeLink(item),
          ),
        ),
      );
  }

  createInvitationLink(
    eventId: number,
    payload: CreateEventInvitationLinkPayload,
  ): Observable<EventInvitationLink> {
    return this.http
      .post<
        ApiEnvelope<InvitationLinkPayload>
      >(`${this.base}/${eventId}/invitation-links`, payload, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeLink(
            requireEnvelopeData(response, 'Invitation link response was incomplete.'),
          ),
        ),
      );
  }

  revokeInvitationLink(eventId: number, linkId: number): Observable<EventInvitationLink> {
    return this.http
      .post<
        ApiEnvelope<InvitationLinkPayload>
      >(`${this.base}/${eventId}/invitation-links/${linkId}/revoke`, {}, { withCredentials: true })
      .pipe(
        map((response) =>
          this.normalizeLink(
            requireEnvelopeData(response, 'Invitation link response was incomplete.'),
          ),
        ),
      );
  }

  private normalizeInvitation(payload: InvitationPayload): EventInvitation {
    return {
      id: payload.id ?? payload.Id ?? 0,
      eventId: payload.eventId ?? payload.EventId ?? 0,
      recipientUserId: payload.recipientUserId ?? payload.RecipientUserId,
      recipientEmail: payload.recipientEmail ?? payload.RecipientEmail,
      sourceType: payload.sourceType ?? payload.SourceType ?? '',
      lifecycleStatus: payload.lifecycleStatus ?? payload.LifecycleStatus ?? '',
      effectiveStatus: payload.effectiveStatus ?? payload.EffectiveStatus ?? '',
      deliveryStatus: payload.deliveryStatus ?? payload.DeliveryStatus ?? '',
      expiresAt: payload.expiresAt ?? payload.ExpiresAt,
      acceptedAtUtc: payload.acceptedAtUtc ?? payload.AcceptedAtUtc,
      declinedAtUtc: payload.declinedAtUtc ?? payload.DeclinedAtUtc,
      revokedAtUtc: payload.revokedAtUtc ?? payload.RevokedAtUtc,
      eventInvitationLinkId: payload.eventInvitationLinkId ?? payload.EventInvitationLinkId,
      deliveryError: payload.deliveryError ?? payload.DeliveryError,
      createdAt: payload.createdAt ?? payload.CreatedAt ?? '',
      updatedAt: payload.updatedAt ?? payload.UpdatedAt ?? '',
      event:
        (payload.event ?? payload.Event)
          ? this.normalizeSummaryEvent(payload.event ?? payload.Event!)
          : undefined,
    };
  }

  private normalizeLink(payload: InvitationLinkPayload): EventInvitationLink {
    return {
      id: payload.id ?? payload.Id ?? 0,
      eventId: payload.eventId ?? payload.EventId ?? 0,
      shareUrl: payload.shareUrl ?? payload.ShareUrl,
      expiresAt: payload.expiresAt ?? payload.ExpiresAt ?? '',
      maxRedemptions: payload.maxRedemptions ?? payload.MaxRedemptions ?? 0,
      redemptionCount: payload.redemptionCount ?? payload.RedemptionCount ?? 0,
      isRevoked: payload.isRevoked ?? payload.IsRevoked ?? false,
      revokedAtUtc: payload.revokedAtUtc ?? payload.RevokedAtUtc,
      createdAt: payload.createdAt ?? payload.CreatedAt ?? '',
      updatedAt: payload.updatedAt ?? payload.UpdatedAt ?? '',
    };
  }

  private normalizeResolve(payload: ResolvePayload): EventInvitationResolve {
    return {
      state: payload.state ?? payload.State ?? '',
      requiresAuthentication:
        payload.requiresAuthentication ?? payload.RequiresAuthentication ?? false,
      canAccept: payload.canAccept ?? payload.CanAccept ?? false,
      canDecline: payload.canDecline ?? payload.CanDecline ?? false,
      sourceType: payload.sourceType ?? payload.SourceType ?? '',
      expiresAt: payload.expiresAt ?? payload.ExpiresAt,
      event:
        (payload.event ?? payload.Event)
          ? this.normalizeSummaryEvent(payload.event ?? payload.Event!)
          : undefined,
    };
  }

  private normalizeDecision(payload: DecisionPayload): EventInvitationDecision {
    const invitationPayload = payload.invitation ?? payload.Invitation;
    if (!invitationPayload) {
      throw new Error('Invitation response was incomplete.');
    }

    return {
      invitation: this.normalizeInvitation(invitationPayload),
    };
  }

  private normalizeSummaryEvent(payload: SummaryEventPayload): EventInvitationSummaryEvent {
    return {
      id: payload.id ?? payload.Id ?? 0,
      name: payload.name ?? payload.Name ?? '',
      description: payload.description ?? payload.Description ?? '',
      location: payload.location ?? payload.Location ?? '',
      isPrivate: payload.isPrivate ?? payload.IsPrivate ?? false,
      registerCost: payload.registerCost ?? payload.RegisterCost ?? 0,
      maxParticipants: payload.maxParticipants ?? payload.MaxParticipants ?? 0,
      registrationCount: payload.registrationCount ?? payload.RegistrationCount ?? 0,
      startTime: payload.startTime ?? payload.StartTime ?? '',
      endTime: payload.endTime ?? payload.EndTime,
      status: payload.status ?? payload.Status ?? '',
      category: payload.category ?? payload.Category ?? '',
      imageUrls: payload.imageUrls ?? payload.ImageUrls ?? [],
      club: payload.club ?? payload.Club,
    };
  }
}
