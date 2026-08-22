import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { map, Observable, switchMap } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import {
  ALL_CATEGORIES,
  ALL_LIFECYCLE_STATES,
  ALL_STATUSES,
  EventCategory,
  EventDraftPayload,
  EventLifecycleState,
  EventLifecycleTransition,
  EventStatus,
  ManageEventsParams,
  ManagedEvent,
  ManagedEventApiResponse,
  ManagedEventsApiResponse,
  ManagedEventsPagedData,
} from '../models/event.types';

type ManagedEventPayload = Partial<ManagedEvent> & {
  Id?: number;
  Name?: string;
  Description?: string;
  Location?: string;
  ImageUrls?: string[];
  IsPrivate?: boolean;
  MaxParticipants?: number;
  RegisterCost?: number;
  StartTime?: string;
  EndTime?: string;
  ClubId?: number;
  CurrentVersionNumber?: number;
  CreatedAt?: string;
  UpdatedAt?: string;
  Status?: string | number;
  LifecycleState?: string | number;
  Category?: string | number;
  VenueName?: string;
  City?: string;
  Latitude?: number;
  Longitude?: number;
  Tags?: string[];
  RegistrationCount?: number;
  WaitlistEnabled?: boolean;
  WaitlistCount?: number;
  PublishReady?: boolean;
  PublishIssues?: string[];
  LifecycleChangedAt?: string | null;
  PreviousLifecycleState?: string | number | null;
  RevertAvailableUntil?: string | null;
  AvailableTransitions?: EventLifecycleTransitionPayload[];
  SeriesId?: number | null;
  OccurrenceIndex?: number | null;
  SeriesOverridden?: boolean;
  TimeZoneId?: string | null;
};

type EventLifecycleTransitionPayload = Partial<EventLifecycleTransition> & {
  Key?: string;
  Target?: string | number;
  Label?: string;
  Title?: string;
  IsReversible?: boolean;
  ReversibleNote?: string | null;
  IsDestructive?: boolean;
  Impacts?: string[];
};

type ManagedEventsPagedPayload = Partial<ManagedEventsPagedData> & {
  Items?: ManagedEventPayload[];
  TotalCount?: number;
  Page?: number;
  PageSize?: number;
  TotalPages?: number;
};

type ManagedEventsApiPayload = ApiEnvelope<ManagedEventsPagedPayload> & {
  Data?: ManagedEventsPagedPayload | null;
};

type ManagedEventApiPayload = ApiEnvelope<ManagedEventPayload> & {
  Data?: ManagedEventPayload | null;
};

type PresignedUploadPayload = ApiEnvelope<{
  uploadUrl?: string;
  UploadUrl?: string;
  publicUrl?: string;
  PublicUrl?: string;
}> & {
  Data?: {
    uploadUrl?: string;
    UploadUrl?: string;
    publicUrl?: string;
    PublicUrl?: string;
  } | null;
};

@Injectable({ providedIn: 'root' })
export class EventsManagementService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private http: HttpClient) {}

  getManageableEvents(
    clubId: number,
    params: ManageEventsParams,
  ): Observable<ManagedEventsApiResponse> {
    let httpParams = new HttpParams();

    if (params.lifecycleState) {
      httpParams = httpParams.set('lifecycleState', params.lifecycleState);
    }
    if (params.page) httpParams = httpParams.set('page', String(params.page));
    if (params.pageSize) httpParams = httpParams.set('pageSize', String(params.pageSize));
    if (params.search?.trim()) httpParams = httpParams.set('search', params.search.trim());

    return this.http
      .get<ManagedEventsApiPayload>(`${this.base}/clubs/${clubId}/manage`, { params: httpParams })
      .pipe(map((response) => this.normalizePagedResponse(response)));
  }

  getManageableEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.http
      .get<ManagedEventApiPayload>(`${this.base}/${eventId}/manage`)
      .pipe(map((response) => this.normalizeEventResponse(response)));
  }

  createDraft(clubId: number, payload: EventDraftPayload): Observable<ManagedEventApiResponse> {
    return this.http
      .post<ManagedEventApiPayload>(`${this.base}/clubs/${clubId}/drafts`, payload)
      .pipe(map((response) => this.normalizeEventResponse(response)));
  }

  updateDraft(eventId: number, payload: EventDraftPayload): Observable<ManagedEventApiResponse> {
    return this.http
      .patch<ManagedEventApiPayload>(`${this.base}/${eventId}/draft`, payload)
      .pipe(map((response) => this.normalizeEventResponse(response)));
  }

  publishEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/publish`);
  }

  cancelEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/cancel`);
  }

  archiveEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/archive`);
  }

  pauseEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/pause`);
  }

  resumeEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/resume`);
  }

  reinstateEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/reinstate`);
  }

  unarchiveEvent(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/unarchive`);
  }

  /**
   * Runs a lifecycle transition by its server-supplied key, so callers can drive
   * `availableTransitions` without a switch that would need editing for every new state.
   */
  runTransition(eventId: number, key: string): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/${key}`);
  }

  /** Undoes the most recent lifecycle change, while the server still offers the window. */
  revertLifecycle(eventId: number): Observable<ManagedEventApiResponse> {
    return this.transition(`${this.base}/${eventId}/lifecycle/revert`);
  }

  deleteEvent(eventId: number): Observable<unknown> {
    return this.http.delete(`${this.base}/${eventId}`);
  }

  uploadImage(clubId: number, file: File, eventId?: number): Observable<string> {
    return this.http
      .post<PresignedUploadPayload>(`${this.base}/images/presigned-url`, {
        clubId,
        eventId,
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
      })
      .pipe(
        switchMap((response) => {
          const payload = response.data ?? response.Data;
          const uploadUrl = payload?.uploadUrl ?? payload?.UploadUrl;
          const publicUrl = payload?.publicUrl ?? payload?.PublicUrl;

          if (!uploadUrl || !publicUrl) {
            throw new Error('The upload URL could not be prepared.');
          }

          return new Observable<string>((subscriber) => {
            void fetch(uploadUrl, {
              method: 'PUT',
              headers: {
                // Azure Blob's "Put Blob" operation requires this header; without it
                // the SAS PUT is rejected with 400 (MissingRequiredHeader).
                'x-ms-blob-type': 'BlockBlob',
                'Content-Type': file.type || 'application/octet-stream',
              },
              body: file,
            })
              .then((uploadResponse) => {
                if (!uploadResponse.ok) {
                  throw new Error('The image upload failed.');
                }

                subscriber.next(publicUrl);
                subscriber.complete();
              })
              .catch((error) => subscriber.error(error));
          });
        }),
      );
  }

  private transition(url: string): Observable<ManagedEventApiResponse> {
    return this.http
      .post<ManagedEventApiPayload>(url, {})
      .pipe(map((response) => this.normalizeEventResponse(response)));
  }

  private normalizePagedResponse(response: ManagedEventsApiPayload): ManagedEventsApiResponse {
    const payload = response.data ?? response.Data ?? null;

    return {
      ...response,
      data: payload ? this.normalizePagedData(payload) : null,
      Data: undefined,
    };
  }

  private normalizeEventResponse(response: ManagedEventApiPayload): ManagedEventApiResponse {
    const payload = response.data ?? response.Data ?? null;

    return {
      ...response,
      data: payload ? this.normalizeEvent(payload) : null,
      Data: undefined,
    };
  }

  private normalizePagedData(payload: ManagedEventsPagedPayload): ManagedEventsPagedData {
    return {
      items: (payload.items ?? payload.Items ?? []).map((item) => this.normalizeEvent(item)),
      totalCount: payload.totalCount ?? payload.TotalCount ?? 0,
      page: payload.page ?? payload.Page ?? 1,
      pageSize: payload.pageSize ?? payload.PageSize ?? 20,
      totalPages: payload.totalPages ?? payload.TotalPages ?? 0,
    };
  }

  private normalizeEvent(item: ManagedEventPayload): ManagedEvent {
    return {
      id: item.id ?? item.Id ?? 0,
      name: item.name ?? item.Name,
      description: item.description ?? item.Description,
      location: item.location ?? item.Location,
      imageUrls: item.imageUrls ?? item.ImageUrls ?? [],
      isPrivate: item.isPrivate ?? item.IsPrivate ?? false,
      maxParticipants: item.maxParticipants ?? item.MaxParticipants,
      registerCost: item.registerCost ?? item.RegisterCost ?? 0,
      startTime: item.startTime ?? item.StartTime,
      endTime: item.endTime ?? item.EndTime,
      clubId: item.clubId ?? item.ClubId ?? 0,
      currentVersionNumber: item.currentVersionNumber ?? item.CurrentVersionNumber ?? 0,
      createdAt: item.createdAt ?? item.CreatedAt ?? '',
      updatedAt: item.updatedAt ?? item.UpdatedAt ?? '',
      status: this.normalizeStatus(item.status ?? item.Status),
      lifecycleState: this.normalizeLifecycle(item.lifecycleState ?? item.LifecycleState),
      category: this.normalizeCategory(item.category ?? item.Category),
      venueName: item.venueName ?? item.VenueName,
      city: item.city ?? item.City,
      latitude: item.latitude ?? item.Latitude,
      longitude: item.longitude ?? item.Longitude,
      tags: item.tags ?? item.Tags ?? [],
      registrationCount: item.registrationCount ?? item.RegistrationCount ?? 0,
      waitlistEnabled: item.waitlistEnabled ?? item.WaitlistEnabled ?? false,
      waitlistCount: item.waitlistCount ?? item.WaitlistCount ?? 0,
      publishReady: item.publishReady ?? item.PublishReady ?? false,
      publishIssues: item.publishIssues ?? item.PublishIssues ?? [],
      seriesId: item.seriesId ?? item.SeriesId ?? null,
      occurrenceIndex: item.occurrenceIndex ?? item.OccurrenceIndex ?? null,
      seriesOverridden: item.seriesOverridden ?? item.SeriesOverridden ?? false,
      timeZoneId: item.timeZoneId ?? item.TimeZoneId ?? null,
      lifecycleChangedAt: item.lifecycleChangedAt ?? item.LifecycleChangedAt ?? null,
      previousLifecycleState: this.normalizeOptionalLifecycle(
        item.previousLifecycleState ?? item.PreviousLifecycleState,
      ),
      revertAvailableUntil: item.revertAvailableUntil ?? item.RevertAvailableUntil ?? null,
      availableTransitions: (item.availableTransitions ?? item.AvailableTransitions ?? []).map(
        (transition) => this.normalizeTransition(transition),
      ),
    };
  }

  private normalizeTransition(
    transition: EventLifecycleTransitionPayload,
  ): EventLifecycleTransition {
    return {
      key: transition.key ?? transition.Key ?? '',
      target: this.normalizeLifecycle(transition.target ?? transition.Target),
      label: transition.label ?? transition.Label ?? '',
      title: transition.title ?? transition.Title ?? '',
      isReversible: transition.isReversible ?? transition.IsReversible ?? false,
      reversibleNote: transition.reversibleNote ?? transition.ReversibleNote ?? null,
      isDestructive: transition.isDestructive ?? transition.IsDestructive ?? false,
      impacts: transition.impacts ?? transition.Impacts ?? [],
    };
  }

  /**
   * Like `normalizeLifecycle`, but keeps null as null: an event that has never changed state
   * has no previous state, which is different from being a draft.
   */
  private normalizeOptionalLifecycle(
    value: string | number | undefined | null,
  ): EventLifecycleState | null {
    return value === undefined || value === null ? null : this.normalizeLifecycle(value);
  }

  private normalizeLifecycle(value: string | number | undefined): EventLifecycleState {
    if (typeof value === 'number') {
      return ALL_LIFECYCLE_STATES[value] ?? 'Draft';
    }

    return ALL_LIFECYCLE_STATES.includes(value as EventLifecycleState)
      ? (value as EventLifecycleState)
      : 'Draft';
  }

  private normalizeStatus(value: string | number | undefined): EventStatus | undefined {
    if (value === undefined || value === null) {
      return undefined;
    }

    if (typeof value === 'number') {
      return ALL_STATUSES[value];
    }

    return ALL_STATUSES.includes(value as EventStatus) ? (value as EventStatus) : undefined;
  }

  private normalizeCategory(value: string | number | undefined): EventCategory {
    if (typeof value === 'number') {
      return ALL_CATEGORIES[value] ?? 'Other';
    }

    return ALL_CATEGORIES.includes(value as EventCategory) ? (value as EventCategory) : 'Other';
  }
}
