import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import {
  ALL_CATEGORIES,
  ALL_STATUSES,
  EventApiResponse,
  EventCategory,
  EventItem,
  EventSearchParams,
  EventsApiResponse,
  EventsPagedData,
  EventStatus,
} from '../models/event.types';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';

type EventItemPayload = EventItem & {
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
  CreatedAt?: string;
  Status?: string | number;
  Category?: string | number;
  VenueName?: string;
  City?: string;
  Latitude?: number;
  Longitude?: number;
  Tags?: string[];
  RegistrationCount?: number;
  DistanceKm?: number;
};

type EventsPagedPayload = Partial<EventsPagedData> & {
  Items?: EventItemPayload[];
  TotalCount?: number;
  Page?: number;
  PageSize?: number;
  TotalPages?: number;
};

type EventsApiPayload = ApiEnvelope<EventsPagedPayload> & {
  Data?: EventsPagedPayload | null;
};

type EventApiPayload = ApiEnvelope<EventItemPayload> & {
  Data?: EventItemPayload | null;
};

@Injectable({ providedIn: 'root' })
export class EventsService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private http: HttpClient) {}

  getEvents(params: EventSearchParams): Observable<EventsApiResponse> {
    let httpParams = new HttpParams();

    if (params.search?.trim()) httpParams = httpParams.set('search', params.search.trim());
    if (params.city?.trim()) httpParams = httpParams.set('city', params.city.trim());
    if (params.category) httpParams = httpParams.set('category', params.category);
    if (params.status) httpParams = httpParams.set('status', params.status);
    if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
    if (params.isPrivate !== undefined) httpParams = httpParams.set('isPrivate', String(params.isPrivate));
    if (params.tags?.trim()) httpParams = httpParams.set('tags', params.tags.trim());
    if (params.lat !== undefined) httpParams = httpParams.set('lat', String(params.lat));
    if (params.lng !== undefined) httpParams = httpParams.set('lng', String(params.lng));
    if (params.radiusKm !== undefined) httpParams = httpParams.set('radiusKm', String(params.radiusKm));
    if (params.page) httpParams = httpParams.set('page', String(params.page));
    if (params.pageSize) httpParams = httpParams.set('pageSize', String(params.pageSize));

    return this.http
      .get<EventsApiPayload>(this.base, { params: httpParams })
      .pipe(map((response) => this.normalizeResponse(response)));
  }

  getEvent(eventId: number): Observable<EventApiResponse> {
    return this.http
      .get<EventApiPayload>(`${this.base}/${eventId}`)
      .pipe(map((response) => this.normalizeEventResponse(response)));
  }

  private normalizeResponse(response: EventsApiPayload): EventsApiResponse {
    const payload = response.data ?? response.Data ?? null;

    return {
      ...response,
      data: payload ? this.normalizePagedData(payload) : null,
      Data: undefined,
    };
  }

  private normalizeEventResponse(response: EventApiPayload): EventApiResponse {
    const payload = response.data ?? response.Data ?? null;

    return {
      ...response,
      data: payload ? this.normalizeEvent(payload) : null,
      Data: undefined,
    };
  }

  private normalizePagedData(payload: EventsPagedPayload): EventsPagedData {
    return {
      items: (payload.items ?? payload.Items ?? []).map((item) => this.normalizeEvent(item)),
      totalCount: payload.totalCount ?? payload.TotalCount ?? 0,
      page: payload.page ?? payload.Page ?? 1,
      pageSize: payload.pageSize ?? payload.PageSize ?? 20,
      totalPages: payload.totalPages ?? payload.TotalPages ?? 0,
    };
  }

  private normalizeEvent(item: EventItemPayload): EventItem {
    return {
      id: item.id ?? item.Id ?? 0,
      name: item.name ?? item.Name ?? '',
      description: item.description ?? item.Description ?? '',
      location: item.location ?? item.Location ?? '',
      imageUrls: item.imageUrls ?? item.ImageUrls ?? [],
      isPrivate: item.isPrivate ?? item.IsPrivate ?? false,
      maxParticipants: item.maxParticipants ?? item.MaxParticipants ?? 0,
      registerCost: item.registerCost ?? item.RegisterCost ?? 0,
      startTime: item.startTime ?? item.StartTime ?? '',
      endTime: item.endTime ?? item.EndTime,
      clubId: item.clubId ?? item.ClubId ?? 0,
      createdAt: item.createdAt ?? item.CreatedAt ?? '',
      status: this.normalizeStatus(item.status ?? item.Status),
      category: this.normalizeCategory(item.category ?? item.Category),
      venueName: item.venueName ?? item.VenueName,
      city: item.city ?? item.City,
      latitude: item.latitude ?? item.Latitude,
      longitude: item.longitude ?? item.Longitude,
      tags: item.tags ?? item.Tags ?? [],
      registrationCount: item.registrationCount ?? item.RegistrationCount ?? 0,
      distanceKm: item.distanceKm ?? item.DistanceKm,
    };
  }

  private normalizeStatus(value: string | number | undefined): EventStatus {
    if (typeof value === 'number') {
      return ALL_STATUSES[value] ?? 'Upcoming';
    }

    return ALL_STATUSES.includes(value as EventStatus) ? (value as EventStatus) : 'Upcoming';
  }

  private normalizeCategory(value: string | number | undefined): EventCategory {
    if (typeof value === 'number') {
      return ALL_CATEGORIES[value] ?? 'Other';
    }

    return ALL_CATEGORIES.includes(value as EventCategory) ? (value as EventCategory) : 'Other';
  }
}
