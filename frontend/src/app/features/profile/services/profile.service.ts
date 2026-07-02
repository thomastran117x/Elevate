import { Injectable } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';

import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { CurrentUserResponse } from '../../../core/models/auth-response.model';
import { environment } from '../../../../environments/environment';

export interface UpdateProfilePayload {
  name?: string;
  username?: string;
  avatar?: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly baseUrl = `${environment.backendUrl}/profile`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
  ) {}

  updateProfile(payload: UpdateProfilePayload): Observable<CurrentUserResponse> {
    return this.patchWithCsrf<ApiEnvelope<CurrentUserResponse>>(this.baseUrl, payload).pipe(
      map((res) => requireEnvelopeData(res, 'Profile update response was incomplete.')),
    );
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.postWithCsrf<void>(`${this.baseUrl}/change-password`, {
      currentPassword,
      newPassword,
    });
  }

  deleteAccount(): Observable<void> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() =>
        this.api.delete<void>(this.baseUrl, {
          withCredentials: true,
        }),
      ),
    );
  }

  private patchWithCsrf<T>(url: string, body: unknown): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() =>
        this.api.patch<T>(url, body, {
          withCredentials: true,
        }),
      ),
    );
  }

  private postWithCsrf<T>(url: string, body: unknown): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() =>
        this.api.post<T>(url, body, {
          withCredentials: true,
        }),
      ),
    );
  }
}
