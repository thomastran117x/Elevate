import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';

export interface RegistrationDetails {
  notes?: string;
  phoneNumber?: string;
  dietaryNeeds?: string;
}

type CheckPayload = {
  isRegistered?: boolean;
  IsRegistered?: boolean;
};

@Injectable({ providedIn: 'root' })
export class EventRegistrationService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private http: HttpClient) {}

  register(eventId: number, details?: RegistrationDetails): Observable<void> {
    return this.http
      .post<ApiEnvelope<unknown>>(
        `${this.base}/${eventId}/register`,
        details ?? {},
        { withCredentials: true },
      )
      .pipe(map(() => void 0));
  }

  unregister(eventId: number): Observable<void> {
    return this.http
      .delete<ApiEnvelope<unknown>>(`${this.base}/${eventId}/register`, {
        withCredentials: true,
      })
      .pipe(map(() => void 0));
  }

  updateRegistration(eventId: number, details: RegistrationDetails): Observable<void> {
    return this.http
      .patch<ApiEnvelope<unknown>>(
        `${this.base}/${eventId}/register`,
        details,
        { withCredentials: true },
      )
      .pipe(map(() => void 0));
  }

  checkRegistration(eventId: number): Observable<boolean> {
    return this.http
      .get<ApiEnvelope<CheckPayload>>(`${this.base}/${eventId}/registrations/me`, {
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const payload =
            response.data ??
            (response as unknown as { Data?: CheckPayload }).Data;
          return payload?.isRegistered ?? payload?.IsRegistered ?? false;
        }),
      );
  }
}
