import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import {
  ClubMemberInvitationDecision,
  ClubMemberInvitationResolve,
  normalizeClubMemberInvitationDecision,
  normalizeClubMemberInvitationResolve,
} from '../models/club-member-invitation.types';

/**
 * Recipient-facing club member invitation flow used by the `/clubs/member-invite` accept page.
 * A single token may be a specific (emailed) invite or a shared link; `resolve` reports which via
 * its `source` field so the page calls `accept`/`decline` (DirectInvite) or `redeemLink` (Link).
 * Organiser-side create/list/revoke live in ClubManagementService.
 */
@Injectable({ providedIn: 'root' })
export class ClubMemberInvitationsService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private http: HttpClient) {}

  resolve(token: string): Observable<ClubMemberInvitationResolve> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/members/invitations/resolve`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubMemberInvitationResolve(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }

  accept(token: string): Observable<ClubMemberInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/members/invitations/accept`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubMemberInvitationDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }

  decline(token: string): Observable<ClubMemberInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/members/invitations/decline`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubMemberInvitationDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }

  redeemLink(token: string): Observable<ClubMemberInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/members/invitation-links/redeem`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubMemberInvitationDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }
}
