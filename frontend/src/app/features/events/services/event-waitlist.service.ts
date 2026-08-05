import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope, extractEnvelopeMeta } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  EventWaitlistEntry,
  EventWaitlistPage,
  JoinWaitlistDetails,
  MyWaitlistStatus,
  WaitlistedEvent,
  WaitlistEntryStatus,
  WaitlistPromotionResult,
} from '../models/event-waitlist.types';
import { EventItem } from '../models/event.types';

type EntryPayload = Partial<EventWaitlistEntry> & {
  Id?: number;
  EventId?: number;
  UserId?: number;
  Position?: number;
  Status?: string;
  JoinedAtUtc?: string;
  PromotedAtUtc?: string | null;
  LeftAtUtc?: string | null;
  RemovedAtUtc?: string | null;
  UserName?: string | null;
  UserEmail?: string | null;
  Notes?: string | null;
  PhoneNumber?: string | null;
  DietaryNeeds?: string | null;
};

type StatusPayload = Partial<MyWaitlistStatus> & {
  OnWaitlist?: boolean;
  EntryId?: number | null;
  Position?: number | null;
  JoinedAtUtc?: string | null;
  WaitlistCount?: number;
};

type WaitlistedEventPayload = {
  entryId?: number;
  EntryId?: number;
  position?: number;
  Position?: number;
  joinedAtUtc?: string;
  JoinedAtUtc?: string;
  accessRevoked?: boolean;
  AccessRevoked?: boolean;
  event?: unknown;
  Event?: unknown;
};

type PromotionPayload = {
  promotedCount?: number;
  PromotedCount?: number;
  promotedUserIds?: number[];
  PromotedUserIds?: number[];
};

type PagedMeta = { totalCount?: number; TotalCount?: number } | null;

const WAITLIST_STATUSES: WaitlistEntryStatus[] = ['Waiting', 'Promoted', 'Left', 'Removed'];

@Injectable({ providedIn: 'root' })
export class EventWaitlistService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private api: ApiClient) {}

  join(eventId: number, details?: JoinWaitlistDetails): Observable<EventWaitlistEntry> {
    return this.api
      .post<ApiEnvelope<EntryPayload>>(`${this.base}/${eventId}/waitlist`, details ?? {}, {
        withCredentials: true,
      })
      .pipe(map((response) => this.normalizeEntry(this.unwrap(response))));
  }

  leave(eventId: number): Observable<void> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.base}/${eventId}/waitlist`, {
        withCredentials: true,
      })
      .pipe(map(() => void 0));
  }

  getMyStatus(eventId: number): Observable<MyWaitlistStatus> {
    return this.api
      .get<ApiEnvelope<StatusPayload>>(`${this.base}/${eventId}/waitlist/me`, {
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const payload = this.unwrap(response);
          return {
            onWaitlist: payload?.onWaitlist ?? payload?.OnWaitlist ?? false,
            entryId: payload?.entryId ?? payload?.EntryId ?? null,
            position: payload?.position ?? payload?.Position ?? null,
            joinedAtUtc: payload?.joinedAtUtc ?? payload?.JoinedAtUtc ?? null,
            waitlistCount: payload?.waitlistCount ?? payload?.WaitlistCount ?? 0,
          };
        }),
      );
  }

  getEventWaitlist(eventId: number, page = 1, pageSize = 20): Observable<EventWaitlistPage> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);

    return this.api
      .get<ApiEnvelope<EntryPayload[], PagedMeta>>(`${this.base}/${eventId}/waitlist`, {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const entries = (this.unwrap(response) ?? []).map((entry) => this.normalizeEntry(entry));
          const meta = extractEnvelopeMeta(response);
          return {
            entries,
            totalCount: meta?.totalCount ?? meta?.TotalCount ?? entries.length,
          };
        }),
      );
  }

  removeEntry(eventId: number, entryId: number): Observable<void> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.base}/${eventId}/waitlist/${entryId}`, {
        withCredentials: true,
      })
      .pipe(map(() => void 0));
  }

  promoteNext(eventId: number): Observable<WaitlistPromotionResult> {
    return this.api
      .post<ApiEnvelope<PromotionPayload>>(
        `${this.base}/${eventId}/waitlist/promote`,
        {},
        { withCredentials: true },
      )
      .pipe(
        map((response) => {
          const payload = this.unwrap(response);
          return {
            promotedCount: payload?.promotedCount ?? payload?.PromotedCount ?? 0,
            promotedUserIds: payload?.promotedUserIds ?? payload?.PromotedUserIds ?? [],
          };
        }),
      );
  }

  getMine(): Observable<WaitlistedEvent[]> {
    return this.api
      .get<ApiEnvelope<WaitlistedEventPayload[]>>(`${this.base}/me/waitlisted`, {
        withCredentials: true,
      })
      .pipe(
        map((response) =>
          (this.unwrap(response) ?? []).map((item) => ({
            entryId: item.entryId ?? item.EntryId ?? 0,
            position: item.position ?? item.Position ?? 0,
            joinedAtUtc: item.joinedAtUtc ?? item.JoinedAtUtc ?? '',
            accessRevoked: item.accessRevoked ?? item.AccessRevoked ?? false,
            event: (item.event ?? item.Event) as EventItem,
          })),
        ),
      );
  }

  private unwrap<T>(response: ApiEnvelope<T, unknown>): T {
    return (response.data ?? (response as unknown as { Data?: T }).Data) as T;
  }

  private normalizeEntry(payload: EntryPayload): EventWaitlistEntry {
    return {
      id: payload?.id ?? payload?.Id ?? 0,
      eventId: payload?.eventId ?? payload?.EventId ?? 0,
      userId: payload?.userId ?? payload?.UserId ?? 0,
      position: payload?.position ?? payload?.Position ?? 0,
      status: this.normalizeStatus(payload?.status ?? payload?.Status),
      joinedAtUtc: payload?.joinedAtUtc ?? payload?.JoinedAtUtc ?? '',
      promotedAtUtc: payload?.promotedAtUtc ?? payload?.PromotedAtUtc ?? null,
      leftAtUtc: payload?.leftAtUtc ?? payload?.LeftAtUtc ?? null,
      removedAtUtc: payload?.removedAtUtc ?? payload?.RemovedAtUtc ?? null,
      userName: payload?.userName ?? payload?.UserName ?? null,
      userEmail: payload?.userEmail ?? payload?.UserEmail ?? null,
      notes: payload?.notes ?? payload?.Notes ?? null,
      phoneNumber: payload?.phoneNumber ?? payload?.PhoneNumber ?? null,
      dietaryNeeds: payload?.dietaryNeeds ?? payload?.DietaryNeeds ?? null,
    };
  }

  private normalizeStatus(value: string | undefined): WaitlistEntryStatus {
    const match = WAITLIST_STATUSES.find(
      (status) => status.toLowerCase() === String(value ?? '').toLowerCase(),
    );

    return match ?? 'Waiting';
  }
}
