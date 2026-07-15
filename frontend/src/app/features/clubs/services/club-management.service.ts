import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { Club, normalizeClub } from '../models/club.types';
import {
  ClubAnalyticsApiResponse,
  ClubMembersApiResponse,
  ClubMutationPayload,
  ClubRollbackApiResponse,
  ClubStaffListApiResponse,
  ClubStaffRole,
  ClubVersionDetailApiResponse,
  ClubVersionsApiResponse,
  ManagedClubsApiResponse,
  normalizeClubAnalytics,
  normalizeClubMembersPagedData,
  normalizeClubRollback,
  normalizeClubStaff,
  normalizeClubVersionDetail,
  normalizeClubVersionsPagedData,
} from '../models/club-management.types';
import { ClubInvitation, normalizeClubInvitation } from '../models/club-invitation.types';
import {
  ClubInvitationLink,
  ClubMemberInvitation,
  normalizeClubInvitationLink,
  normalizeClubMemberInvitation,
} from '../models/club-member-invitation.types';

@Injectable({ providedIn: 'root' })
export class ClubManagementService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private api: ApiClient) {}

  private rawData(response: ApiEnvelope<unknown>): unknown {
    return (
      (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
      (response as { Data?: unknown }).Data ??
      null
    );
  }

  getManagedClubs(): Observable<ManagedClubsApiResponse> {
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/managed`, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response) as unknown[] | null;
          return {
            ...response,
            data: raw
              ? raw.map((c) => normalizeClub(c as Parameters<typeof normalizeClub>[0]))
              : [],
          } as ManagedClubsApiResponse;
        }),
      );
  }

  createClub(payload: ClubMutationPayload): Observable<ApiEnvelope<Club>> {
    return this.api
      .post<ApiEnvelope<unknown>>(this.base, payload, { withCredentials: true })
      .pipe(map((response) => this.mapClub(response)));
  }

  updateClub(clubId: number, payload: ClubMutationPayload): Observable<ApiEnvelope<Club>> {
    return this.api
      .put<ApiEnvelope<unknown>>(`${this.base}/${clubId}`, payload, { withCredentials: true })
      .pipe(map((response) => this.mapClub(response)));
  }

  getMembers(
    clubId: number,
    page = 1,
    pageSize = 20,
    search?: string,
  ): Observable<ClubMembersApiResponse> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (search?.trim()) params = params.set('search', search.trim());
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/members`, {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubMembersPagedData(
                  raw as Parameters<typeof normalizeClubMembersPagedData>[0],
                )
              : null,
          } as ClubMembersApiResponse;
        }),
      );
  }

  getStaff(clubId: number, search?: string): Observable<ClubStaffListApiResponse> {
    let params = new HttpParams();
    if (search?.trim()) params = params.set('search', search.trim());
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/staff`, { params, withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response) as unknown[] | null;
          return {
            ...response,
            data: raw
              ? raw.map((s) => normalizeClubStaff(s as Parameters<typeof normalizeClubStaff>[0]))
              : [],
          } as ClubStaffListApiResponse;
        }),
      );
  }

  /**
   * Invites an existing user (by username or email) to become staff. The backend resolves the
   * identifier, and if the account exists, emails them a recipient-bound invitation to accept.
   */
  inviteStaff(
    clubId: number,
    identifier: string,
    role: ClubStaffRole,
  ): Observable<ApiEnvelope<ClubInvitation>> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/staff/invitations`, { identifier, role }, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubInvitation(raw as Parameters<typeof normalizeClubInvitation>[0])
              : null,
          } as ApiEnvelope<ClubInvitation>;
        }),
      );
  }

  getStaffInvitations(clubId: number): Observable<ApiEnvelope<ClubInvitation[]>> {
    return this.api
      .get<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/staff/invitations`, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response) as unknown[] | null;
          return {
            ...response,
            data: raw
              ? raw.map((i) =>
                  normalizeClubInvitation(i as Parameters<typeof normalizeClubInvitation>[0]),
                )
              : [],
          } as ApiEnvelope<ClubInvitation[]>;
        }),
      );
  }

  revokeStaffInvitation(clubId: number, recipientUserId: number): Observable<ApiEnvelope<unknown>> {
    return this.api.post<ApiEnvelope<unknown>>(
      `${this.base}/${clubId}/staff/invitations/${recipientUserId}/revoke`,
      {},
      { withCredentials: true },
    );
  }

  // ---- Member invitations (specific, emailed) ----

  /** Invites an existing user (by username or email) to become a member; the backend emails them. */
  inviteMember(clubId: number, identifier: string): Observable<ApiEnvelope<ClubMemberInvitation>> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/members/invitations`, { identifier }, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubMemberInvitation(
                  raw as Parameters<typeof normalizeClubMemberInvitation>[0],
                )
              : null,
          } as ApiEnvelope<ClubMemberInvitation>;
        }),
      );
  }

  getMemberInvitations(clubId: number): Observable<ApiEnvelope<ClubMemberInvitation[]>> {
    return this.api
      .get<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/members/invitations`, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response) as unknown[] | null;
          return {
            ...response,
            data: raw
              ? raw.map((i) =>
                  normalizeClubMemberInvitation(
                    i as Parameters<typeof normalizeClubMemberInvitation>[0],
                  ),
                )
              : [],
          } as ApiEnvelope<ClubMemberInvitation[]>;
        }),
      );
  }

  revokeMemberInvitation(
    clubId: number,
    recipientUserId: number,
  ): Observable<ApiEnvelope<unknown>> {
    return this.api.post<ApiEnvelope<unknown>>(
      `${this.base}/${clubId}/members/invitations/${recipientUserId}/revoke`,
      {},
      { withCredentials: true },
    );
  }

  // ---- Member invite links (shareable, no email) ----

  createMemberInviteLink(
    clubId: number,
    expiresAt: string,
    maxRedemptions: number | null,
  ): Observable<ApiEnvelope<ClubInvitationLink>> {
    const body: { expiresAt: string; maxRedemptions?: number } = { expiresAt };
    if (maxRedemptions != null) {
      body.maxRedemptions = maxRedemptions;
    }
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/members/invitation-links`, body, { withCredentials: true })
      .pipe(map((response) => this.mapLink(response)));
  }

  getMemberInviteLinks(clubId: number): Observable<ApiEnvelope<ClubInvitationLink[]>> {
    return this.api
      .get<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/members/invitation-links`, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response) as unknown[] | null;
          return {
            ...response,
            data: raw
              ? raw.map((l) =>
                  normalizeClubInvitationLink(
                    l as Parameters<typeof normalizeClubInvitationLink>[0],
                  ),
                )
              : [],
          } as ApiEnvelope<ClubInvitationLink[]>;
        }),
      );
  }

  revokeMemberInviteLink(
    clubId: number,
    linkId: number,
  ): Observable<ApiEnvelope<ClubInvitationLink>> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/members/invitation-links/${linkId}/revoke`, {}, { withCredentials: true })
      .pipe(map((response) => this.mapLink(response)));
  }

  private mapLink(response: ApiEnvelope<unknown>): ApiEnvelope<ClubInvitationLink> {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw
        ? normalizeClubInvitationLink(raw as Parameters<typeof normalizeClubInvitationLink>[0])
        : null,
    } as ApiEnvelope<ClubInvitationLink>;
  }

  removeStaff(clubId: number, userId: number): Observable<ApiEnvelope<unknown>> {
    return this.api.delete<ApiEnvelope<unknown>>(`${this.base}/${clubId}/staff/${userId}`, {
      withCredentials: true,
    });
  }

  /** Transfer ownership to an existing user identified by username or email. */
  transferOwnership(clubId: number, newOwnerIdentifier: string): Observable<ApiEnvelope<Club>> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/transfer-ownership`, { newOwnerIdentifier }, { withCredentials: true })
      .pipe(map((response) => this.mapClub(response)));
  }

  deleteClub(clubId: number): Observable<ApiEnvelope<unknown>> {
    return this.api.delete<ApiEnvelope<unknown>>(`${this.base}/${clubId}`, {
      withCredentials: true,
    });
  }

  getVersions(clubId: number, page = 1, pageSize = 20): Observable<ClubVersionsApiResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/versions`, {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubVersionsPagedData(
                  raw as Parameters<typeof normalizeClubVersionsPagedData>[0],
                )
              : null,
          } as ClubVersionsApiResponse;
        }),
      );
  }

  getVersion(clubId: number, versionNumber: number): Observable<ClubVersionDetailApiResponse> {
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/versions/${versionNumber}`, {
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubVersionDetail(raw as Parameters<typeof normalizeClubVersionDetail>[0])
              : null,
          } as ClubVersionDetailApiResponse;
        }),
      );
  }

  rollback(clubId: number, versionNumber: number): Observable<ClubRollbackApiResponse> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/versions/${versionNumber}/rollback`, {}, { withCredentials: true })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubRollback(raw as Parameters<typeof normalizeClubRollback>[0])
              : null,
          } as ClubRollbackApiResponse;
        }),
      );
  }

  getAnalytics(clubId: number): Observable<ClubAnalyticsApiResponse> {
    return this.api
      .get<ApiEnvelope<unknown>>(`${environment.backendUrl}/events/clubs/${clubId}/analytics`, {
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubAnalytics(raw as Parameters<typeof normalizeClubAnalytics>[0])
              : null,
          } as ClubAnalyticsApiResponse;
        }),
      );
  }

  private mapClub(response: ApiEnvelope<unknown>): ApiEnvelope<Club> {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw ? normalizeClub(raw as Parameters<typeof normalizeClub>[0]) : null,
    } as ApiEnvelope<Club>;
  }
}
