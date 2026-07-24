import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  ClubReview,
  ClubReviewsApiResponse,
  normalizeClubReview,
  normalizeClubReviewsPagedData,
} from '../models/club-review.types';

export interface ClubReviewMutation {
  title: string;
  rating: number;
  comment?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ClubReviewsService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private api: ApiClient) {}

  getReviews(clubId: number, page = 1, pageSize = 20): Observable<ClubReviewsApiResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/reviews`, {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw = this.rawData(response);
          return {
            ...response,
            data: raw
              ? normalizeClubReviewsPagedData(
                  raw as Parameters<typeof normalizeClubReviewsPagedData>[0],
                )
              : null,
          } as ClubReviewsApiResponse;
        }),
      );
  }

  createReview(clubId: number, payload: ClubReviewMutation): Observable<ApiEnvelope<ClubReview>> {
    return this.api
      .post<ApiEnvelope<unknown>>(`${this.base}/${clubId}/reviews`, payload, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapReview(response)));
  }

  updateReview(
    clubId: number,
    reviewId: number,
    payload: ClubReviewMutation,
  ): Observable<ApiEnvelope<ClubReview>> {
    return this.api
      .put<ApiEnvelope<unknown>>(`${this.base}/${clubId}/reviews/${reviewId}`, payload, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapReview(response)));
  }

  deleteReview(clubId: number, reviewId: number): Observable<ApiEnvelope<unknown>> {
    return this.api.delete<ApiEnvelope<unknown>>(`${this.base}/${clubId}/reviews/${reviewId}`, {
      withCredentials: true,
    });
  }

  private mapReview(response: ApiEnvelope<unknown>): ApiEnvelope<ClubReview> {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw ? normalizeClubReview(raw as Parameters<typeof normalizeClubReview>[0]) : null,
    } as ApiEnvelope<ClubReview>;
  }

  private rawData(response: ApiEnvelope<unknown>): unknown {
    return (
      (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
      (response as { Data?: unknown }).Data ??
      null
    );
  }
}
