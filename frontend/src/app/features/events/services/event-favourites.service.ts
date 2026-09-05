import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { EventFavourite, PinnedEvent } from '../models/event-favourite.types';
import { EventItemPayload, normalizeEventItem } from '../models/event-normalizers';

type FavouritePayload = Partial<EventFavourite> & {
  EventId?: number;
  IsFavourited?: boolean;
  FavouritedAtUtc?: string | null;
};

type PinnedEventPayload = {
  isRegistered?: boolean;
  IsRegistered?: boolean;
  isFavourited?: boolean;
  IsFavourited?: boolean;
  favouritedAtUtc?: string | null;
  FavouritedAtUtc?: string | null;
  registeredAtUtc?: string | null;
  RegisteredAtUtc?: string | null;
  accessRevoked?: boolean;
  AccessRevoked?: boolean;
  event?: unknown;
  Event?: unknown;
};

@Injectable({ providedIn: 'root' })
export class EventFavouritesService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private api: ApiClient) {}

  favourite(eventId: number): Observable<EventFavourite> {
    return this.api
      .post<ApiEnvelope<FavouritePayload>>(
        `${this.base}/${eventId}/favourite`,
        {},
        { withCredentials: true },
      )
      .pipe(map((response) => this.normalizeFavourite(eventId, this.unwrap(response))));
  }

  unfavourite(eventId: number): Observable<void> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.base}/${eventId}/favourite`, {
        withCredentials: true,
      })
      .pipe(map(() => void 0));
  }

  getMyStatus(eventId: number): Observable<EventFavourite> {
    return this.api
      .get<ApiEnvelope<FavouritePayload>>(`${this.base}/${eventId}/favourite/me`, {
        withCredentials: true,
      })
      .pipe(map((response) => this.normalizeFavourite(eventId, this.unwrap(response))));
  }

  /**
   * Just the ids, so list views can render star state in one request rather than every event
   * payload carrying a per-user flag.
   */
  getMyFavouriteIds(): Observable<number[]> {
    return this.api
      .get<ApiEnvelope<number[]>>(`${this.base}/me/favourites/ids`, {
        withCredentials: true,
      })
      .pipe(map((response) => this.unwrap(response) ?? []));
  }

  getMyPinned(): Observable<PinnedEvent[]> {
    return this.api
      .get<ApiEnvelope<PinnedEventPayload[]>>(`${this.base}/me/pinned`, {
        withCredentials: true,
      })
      .pipe(
        map((response) =>
          (this.unwrap(response) ?? []).map((item) => ({
            isRegistered: item.isRegistered ?? item.IsRegistered ?? false,
            isFavourited: item.isFavourited ?? item.IsFavourited ?? false,
            favouritedAtUtc: item.favouritedAtUtc ?? item.FavouritedAtUtc ?? null,
            registeredAtUtc: item.registeredAtUtc ?? item.RegisteredAtUtc ?? null,
            accessRevoked: item.accessRevoked ?? item.AccessRevoked ?? false,
            event: normalizeEventItem((item.event ?? item.Event) as EventItemPayload),
          })),
        ),
      );
  }

  private unwrap<T>(response: ApiEnvelope<T, unknown>): T {
    return (response.data ?? (response as unknown as { Data?: T }).Data) as T;
  }

  private normalizeFavourite(eventId: number, payload: FavouritePayload): EventFavourite {
    return {
      eventId: payload?.eventId ?? payload?.EventId ?? eventId,
      isFavourited: payload?.isFavourited ?? payload?.IsFavourited ?? false,
      favouritedAtUtc: payload?.favouritedAtUtc ?? payload?.FavouritedAtUtc ?? null,
    };
  }
}
