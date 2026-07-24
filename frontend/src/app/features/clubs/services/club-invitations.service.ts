import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import {
  ClubInvitationDecision,
  ClubInvitationResolve,
  normalizeClubInvitationDecision,
  normalizeClubInvitationResolve,
} from '../models/club-invitation.types';

/**
 * Recipient-facing club staff invitation flow (resolve / accept / decline) used by the
 * `/clubs/invite` accept page. Owner-side create/list/revoke live in ClubManagementService.
 */
@Injectable({ providedIn: 'root' })
export class ClubInvitationsService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private http: HttpClient) {}

  resolve(token: string): Observable<ClubInvitationResolve> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/invitations/resolve`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubInvitationResolve(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }

  accept(token: string): Observable<ClubInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/invitations/accept`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubInvitationDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }

  decline(token: string): Observable<ClubInvitationDecision> {
    return this.http
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/invitations/decline`, { token }, { withCredentials: true })
      .pipe(
        map((response) =>
          normalizeClubInvitationDecision(
            requireEnvelopeData(response, 'Invitation response was incomplete.') as never,
          ),
        ),
      );
  }
}
