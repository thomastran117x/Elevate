import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { ClubReviewsApiResponse, normalizeClubReviewsPagedData } from '../models/club-review.types';

@Injectable({ providedIn: 'root' })
export class ClubReviewsService {
  constructor(private api: ApiClient) {}

  getReviews(clubId: number, page = 1, pageSize = 20): Observable<ClubReviewsApiResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    return this.api
      .get<ApiEnvelope<unknown>>(`${environment.backendUrl}/clubs/${clubId}/reviews`, {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const raw =
            (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
            (response as { Data?: unknown }).Data ??
            null;
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
}
