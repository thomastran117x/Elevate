import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import {
  ClubPost,
  ClubPostsApiResponse,
  ClubPostsPagedData,
  PostSortBy,
  normalizeClubPost,
  normalizeClubPostsPagedData,
} from '../models/club-post.types';

export interface ClubPostsSearchParams {
  search?: string;
  sortBy?: PostSortBy;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ClubPostsService {
  constructor(private http: HttpClient) {}

  getPosts(clubId: number, params: ClubPostsSearchParams = {}): Observable<ClubPostsApiResponse> {
    let httpParams = new HttpParams();
    if (params.search?.trim()) httpParams = httpParams.set('search', params.search.trim());
    if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
    if (params.page) httpParams = httpParams.set('page', String(params.page));
    if (params.pageSize) httpParams = httpParams.set('pageSize', String(params.pageSize));

    return this.http
      .get<ApiEnvelope<unknown>>(`${environment.backendUrl}/clubs/${clubId}/posts`, {
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
              ? normalizeClubPostsPagedData(
                  raw as Parameters<typeof normalizeClubPostsPagedData>[0],
                )
              : null,
          } as ClubPostsApiResponse;
        }),
      );
  }

  getPost(clubId: number, postId: number): Observable<ApiEnvelope<ClubPost>> {
    return this.http
      .get<ApiEnvelope<unknown>>(`${environment.backendUrl}/clubs/${clubId}/posts/${postId}`, {
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
            data: raw ? normalizeClubPost(raw as Parameters<typeof normalizeClubPost>[0]) : null,
          } as ApiEnvelope<ClubPost>;
        }),
      );
  }
}
