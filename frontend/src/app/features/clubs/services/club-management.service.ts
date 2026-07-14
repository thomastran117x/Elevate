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

  getMembers(clubId: number, page = 1, pageSize = 20): Observable<ClubMembersApiResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
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

  getStaff(clubId: number): Observable<ClubStaffListApiResponse> {
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/staff`, { withCredentials: true })
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
              ? raw.map((i) => normalizeClubInvitation(i as Parameters<typeof normalizeClubInvitation>[0]))
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

  removeStaff(clubId: number, userId: number): Observable<ApiEnvelope<unknown>> {
    return this.api.delete<ApiEnvelope<unknown>>(`${this.base}/${clubId}/staff/${userId}`, {
      withCredentials: true,
    });
  }

  transferOwnership(clubId: number, newOwnerUserId: number): Observable<ApiEnvelope<Club>> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${clubId}/transfer-ownership`, { newOwnerUserId }, { withCredentials: true })
      .pipe(map((response) => this.mapClub(response)));
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
