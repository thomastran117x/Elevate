import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import {
  EventSeries,
  EventSeriesDeleteScope,
  EventSeriesSummary,
  ManagedEvent,
  RecurrenceRule,
  SeriesBulkResult,
  SeriesPreview,
  UpdateFutureOccurrencesPayload,
} from '../models/event.types';

interface PagedData<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

@Injectable({ providedIn: 'root' })
export class EventSeriesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.backendUrl}/events`;

  /**
   * Expands a rule without saving. Drives the wizard's live preview, which is where the
   * time zone behaviour is made visible before an organizer commits to it.
   */
  previewSeries(
    clubId: number,
    recurrence: RecurrenceRule,
  ): Observable<ApiEnvelope<SeriesPreview>> {
    return this.http.post<ApiEnvelope<SeriesPreview>>(
      `${this.base}/clubs/${clubId}/series/preview`,
      { recurrence },
    );
  }

  createSeries(eventId: number, recurrence: RecurrenceRule): Observable<ApiEnvelope<EventSeries>> {
    return this.http.post<ApiEnvelope<EventSeries>>(`${this.base}/${eventId}/series`, {
      recurrence,
    });
  }

  getSeries(seriesId: number): Observable<ApiEnvelope<EventSeries>> {
    return this.http.get<ApiEnvelope<EventSeries>>(`${this.base}/series/${seriesId}`);
  }

  getClubSeries(
    clubId: number,
    page = 1,
    pageSize = 20,
  ): Observable<ApiEnvelope<PagedData<EventSeriesSummary>>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    return this.http.get<ApiEnvelope<PagedData<EventSeriesSummary>>>(
      `${this.base}/clubs/${clubId}/series`,
      { params },
    );
  }

  extendSeries(
    seriesId: number,
    payload: { occurrenceCount?: number; untilLocalDate?: string },
  ): Observable<ApiEnvelope<EventSeries>> {
    return this.http.post<ApiEnvelope<EventSeries>>(
      `${this.base}/series/${seriesId}/extend`,
      payload,
    );
  }

  publishSeries(seriesId: number): Observable<ApiEnvelope<SeriesBulkResult>> {
    return this.http.post<ApiEnvelope<SeriesBulkResult>>(
      `${this.base}/series/${seriesId}/publish`,
      {},
    );
  }

  /** Applies a patch to the given occurrence and every later one that has not yet started. */
  updateFutureOccurrences(
    seriesId: number,
    payload: UpdateFutureOccurrencesPayload,
  ): Observable<ApiEnvelope<SeriesBulkResult>> {
    return this.http.patch<ApiEnvelope<SeriesBulkResult>>(
      `${this.base}/series/${seriesId}/occurrences`,
      payload,
    );
  }

  cancelSeries(seriesId: number, futureOnly = true): Observable<ApiEnvelope<SeriesBulkResult>> {
    return this.http.post<ApiEnvelope<SeriesBulkResult>>(`${this.base}/series/${seriesId}/cancel`, {
      futureOnly,
    });
  }

  deleteSeries(
    seriesId: number,
    scope: EventSeriesDeleteScope = 'FutureDrafts',
  ): Observable<ApiEnvelope<SeriesBulkResult>> {
    return this.http.delete<ApiEnvelope<SeriesBulkResult>>(`${this.base}/series/${seriesId}`, {
      body: { scope },
    });
  }

  detachOccurrence(eventId: number): Observable<ApiEnvelope<ManagedEvent>> {
    return this.http.post<ApiEnvelope<ManagedEvent>>(`${this.base}/${eventId}/series/detach`, {});
  }

  /**
   * Turns a set of skipped occurrences into a single sentence for the UI. Partial success is
   * the normal outcome of a series-wide edit, so it needs to read as information, not failure.
   */
  describeSkipped(result: SeriesBulkResult): string | null {
    if (!result.skipped.length) {
      return null;
    }

    const reasons = new Set(result.skipped.map((entry) => entry.details[0]).filter(Boolean));
    const count = result.skipped.length;
    const noun = count === 1 ? 'occurrence was' : 'occurrences were';

    return `${count} ${noun} left unchanged. ${[...reasons].join(' ')}`.trim();
  }
}
