import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  ClubDiscussion,
  ClubDiscussionApiResponse,
  ClubDiscussionsApiResponse,
  normalizeClubDiscussion,
  normalizeClubDiscussionsPagedData,
} from '../models/club-discussion.types';

export interface ClubDiscussionMutation {
  title: string;
  description: string;
}

@Injectable({ providedIn: 'root' })
export class ClubDiscussionsService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private api: ApiClient) {}

  /** Lists a club's discussions, newest first (ordering comes from the API). */
  getDiscussions(clubId: number, page = 1, pageSize = 20): Observable<ClubDiscussionsApiResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/discussions`, {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubDiscussionsPagedData(
                  raw as Parameters<typeof normalizeClubDiscussionsPagedData>[0],
                )
              : null,
          } as ClubDiscussionsApiResponse;
        }),
      );
  }

  createDiscussion(
    clubId: number,
    payload: ClubDiscussionMutation,
  ): Observable<ClubDiscussionApiResponse> {
    return this.api
      .post<ApiEnvelope<unknown>>(`${this.base}/${clubId}/discussions`, payload, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapDiscussion(response)));
  }

  updateDiscussion(
    clubId: number,
    discussionId: number,
    payload: ClubDiscussionMutation,
  ): Observable<ClubDiscussionApiResponse> {
    return this.api
      .put<ApiEnvelope<unknown>>(`${this.base}/${clubId}/discussions/${discussionId}`, payload, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapDiscussion(response)));
  }

  deleteDiscussion(clubId: number, discussionId: number): Observable<ApiEnvelope<unknown>> {
    return this.api.delete<ApiEnvelope<unknown>>(
      `${this.base}/${clubId}/discussions/${discussionId}`,
      { withCredentials: true },
    );
  }

  private mapDiscussion(response: ApiEnvelope<unknown>): ClubDiscussionApiResponse {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw
        ? normalizeClubDiscussion(raw as Parameters<typeof normalizeClubDiscussion>[0])
        : null,
    } as ApiEnvelope<ClubDiscussion>;
  }

  private rawData(response: ApiEnvelope<unknown>): unknown {
    return (
      (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
      (response as { Data?: unknown }).Data ??
      null
    );
  }
}
