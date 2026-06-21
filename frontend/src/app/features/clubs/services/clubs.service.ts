import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  Club,
  ClubApiResponse,
  ClubsApiResponse,
  ClubSortBy,
  ClubType,
  normalizeClub,
  normalizeClubsPagedData,
} from '../models/club.types';

export interface ClubsSearchParams {
  search?: string;
  clubType?: ClubType;
  sortBy?: ClubSortBy;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ClubsService {
  constructor(private api: ApiClient) {}

  getClubs(params: ClubsSearchParams = {}): Observable<ClubsApiResponse> {
    let httpParams = new HttpParams();
    if (params.search?.trim()) httpParams = httpParams.set('search', params.search.trim());
    if (params.clubType) httpParams = httpParams.set('clubType', params.clubType);
    if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
    if (params.page) httpParams = httpParams.set('page', String(params.page));
    if (params.pageSize) httpParams = httpParams.set('pageSize', String(params.pageSize));

    return this.api
      .get<ApiEnvelope<unknown>>(`${environment.backendUrl}/clubs`, {
        params: httpParams,
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
              ? normalizeClubsPagedData(raw as Parameters<typeof normalizeClubsPagedData>[0])
              : null,
          } as ClubsApiResponse;
        }),
      );
  }

  getClub(clubId: number): Observable<ClubApiResponse> {
    return this.api
      .get<ApiEnvelope<unknown>>(`${environment.backendUrl}/clubs/${clubId}`, {
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
            data: raw ? normalizeClub(raw as Parameters<typeof normalizeClub>[0]) : null,
          } as ClubApiResponse;
        }),
      );
  }
}
