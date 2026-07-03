import { Injectable } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';

import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { environment } from '../../../../environments/environment';

export interface UpdateProfilePayload {
  name?: string;
  username?: string;
  avatar?: string;
  phone?: string;
  address?: string;
}

export interface MyProfile {
  Id: number;
  Email: string;
  Username: string;
  Name?: string | null;
  Avatar?: string | null;
  Usertype: string;
  Phone?: string | null;
  Address?: string | null;
  GoogleLinked: boolean;
  MicrosoftLinked: boolean;
  CreatedAtUtc: string;
  UpdatedAtUtc: string;
}

export interface PublicProfile {
  Id: number;
  Username: string;
  Name?: string | null;
  Avatar?: string | null;
  Usertype: string;
  CreatedAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly baseUrl = `${environment.backendUrl}/profile`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
  ) {}

  getMyProfile(): Observable<MyProfile> {
    return this.getWithCsrf<ApiEnvelope<MyProfile>>(this.baseUrl).pipe(
      map((res) => requireEnvelopeData(res, 'Profile response was incomplete.')),
    );
  }

  getPublicProfile(username: string): Observable<PublicProfile> {
    return this.api
      .get<ApiEnvelope<PublicProfile>>(`${this.baseUrl}/${encodeURIComponent(username)}`)
      .pipe(map((res) => requireEnvelopeData(res, 'Profile response was incomplete.')));
  }

  updateProfile(payload: UpdateProfilePayload): Observable<MyProfile> {
    return this.patchWithCsrf<ApiEnvelope<MyProfile>>(this.baseUrl, payload).pipe(
      map((res) => requireEnvelopeData(res, 'Profile update response was incomplete.')),
    );
  }

  uploadAvatar(file: File): Observable<MyProfile> {
    const formData = new FormData();
    formData.append('image', file);
    return this.postWithCsrf<ApiEnvelope<MyProfile>>(`${this.baseUrl}/avatar`, formData).pipe(
      map((res) => requireEnvelopeData(res, 'Avatar upload response was incomplete.')),
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

  private getWithCsrf<T>(url: string): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() =>
        this.api.get<T>(url, {
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
