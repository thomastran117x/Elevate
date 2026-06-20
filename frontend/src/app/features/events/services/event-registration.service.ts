import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';

export interface RegistrationDetails {
  notes?: string;
  phoneNumber?: string;
  dietaryNeeds?: string;
}

export interface MyRegistrationStatus {
  isRegistered: boolean;
  details: RegistrationDetails | null;
}

type CheckPayload = {
  isRegistered?: boolean;
  IsRegistered?: boolean;
  notes?: string;
  Notes?: string;
  phoneNumber?: string;
  PhoneNumber?: string;
  dietaryNeeds?: string;
  DietaryNeeds?: string;
};

@Injectable({ providedIn: 'root' })
export class EventRegistrationService {
  private readonly base = `${environment.backendUrl}/events`;

  constructor(private api: ApiClient) {}

  register(eventId: number, details?: RegistrationDetails): Observable<void> {
    return this.api
      .post<
        ApiEnvelope<unknown>
      >(`${this.base}/${eventId}/register`, details ?? {}, { withCredentials: true })
      .pipe(map(() => void 0));
  }

  unregister(eventId: number): Observable<void> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.base}/${eventId}/register`, {
        withCredentials: true,
      })
      .pipe(map(() => void 0));
  }

  updateRegistration(eventId: number, details: RegistrationDetails): Observable<void> {
    return this.api
      .patch<
        ApiEnvelope<unknown>
      >(`${this.base}/${eventId}/register`, details, { withCredentials: true })
      .pipe(map(() => void 0));
  }

  checkRegistration(eventId: number): Observable<MyRegistrationStatus> {
    return this.api
      .get<ApiEnvelope<CheckPayload>>(`${this.base}/${eventId}/registrations/me`, {
        withCredentials: true,
      })
      .pipe(
        map((response) => {
          const payload = response.data ?? (response as unknown as { Data?: CheckPayload }).Data;
          const isRegistered = payload?.isRegistered ?? payload?.IsRegistered ?? false;
          return {
            isRegistered,
            details: isRegistered
              ? {
                  notes: payload?.notes ?? payload?.Notes ?? undefined,
                  phoneNumber: payload?.phoneNumber ?? payload?.PhoneNumber ?? undefined,
                  dietaryNeeds: payload?.dietaryNeeds ?? payload?.DietaryNeeds ?? undefined,
                }
              : null,
          };
        }),
      );
  }
}
