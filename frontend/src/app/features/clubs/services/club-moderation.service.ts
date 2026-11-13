import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  ClubModerationLogApiResponse,
  ClubModerationLogPagePayload,
  normalizeClubModerationLogPage,
} from '../models/club-moderation.types';

/** Reads a club's moderation log, which its managers, owner and admins can see. */
@Injectable({ providedIn: 'root' })
export class ClubModerationService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private api: ApiClient) {}

  getModerationLog(
    clubId: number,
    page = 1,
    pageSize = 20,
  ): Observable<ClubModerationLogApiResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${clubId}/moderation-log`, {
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
            data: raw ? normalizeClubModerationLogPage(raw as ClubModerationLogPagePayload) : null,
          } as ClubModerationLogApiResponse;
        }),
      );
  }
}
