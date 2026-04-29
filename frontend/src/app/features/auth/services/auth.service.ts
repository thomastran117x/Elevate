import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { from, switchMap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';

export interface LoginRequest {
  email: string;
  password: string;
  remember: boolean;
}

export interface SignupRequest {
  email: string;
  password: string;
  role: 'participant' | 'organizer';
}

export interface AuthResponse {
  Username: string;
  Token: string;
  Avatar: string;
  Usertype: string;
  Id: number;
}

export interface ApiEnvelope<T> {
  message?: string;
  Message?: string;
  data?: T;
  Data?: T;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly baseUrl = `${environment.backendUrl}/auth`;

  constructor(
    private http: HttpClient,
    private authToken: AuthTokenService,
  ) {}

  login(payload: LoginRequest): Observable<AuthResponse> {
    return this.postWithCsrf<AuthResponse>(`${this.baseUrl}/login`, payload);
  }

  signup(payload: SignupRequest): Observable<{ message: string }> {
    return this.postWithCsrf<{ message: string }>(`${this.baseUrl}/signup`, payload);
  }

  verifyEmail(token: string): Observable<ApiEnvelope<AuthResponse>> {
    return this.postWithCsrf<ApiEnvelope<AuthResponse>>(`${this.baseUrl}/verify`, { token });
  }

  verifyDevice(token: string): Observable<ApiEnvelope<AuthResponse>> {
    return this.postWithCsrf<ApiEnvelope<AuthResponse>>(`${this.baseUrl}/device/verify`, { token });
  }

  googleVerify(idToken: string): Observable<AuthResponse> {
    return this.postWithCsrf<AuthResponse>(`${this.baseUrl}/google`, { Token: idToken });
  }

  microsoftVerify(idToken: string): Observable<AuthResponse> {
    return this.postWithCsrf<AuthResponse>(`${this.baseUrl}/microsoft`, { Token: idToken });
  }

  logout(): Observable<void> {
    return this.postWithCsrf<void>(`${this.baseUrl}/logout`, {});
  }

  get googleOAuthUrl(): string {
    return `${this.baseUrl}/login/google`;
  }

  get microsoftOAuthUrl(): string {
    return `${this.baseUrl}/login/microsoft`;
  }

  private postWithCsrf<T>(url: string, body: unknown): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() => {
        return this.http.post<T>(url, body, {
          withCredentials: true,
        });
      }),
    );
  }
}
