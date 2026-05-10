import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { EventSearchParams, EventsApiResponse } from '../models/event.types';

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
    if (params.tags) httpParams = httpParams.set('tags', params.tags);
    if (params.page) httpParams = httpParams.set('page', String(params.page));
    if (params.pageSize) httpParams = httpParams.set('pageSize', String(params.pageSize));

    return this.http.get<EventsApiResponse>(this.base, { params: httpParams });
  }
}
