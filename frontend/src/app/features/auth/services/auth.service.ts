import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { from, map, switchMap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  AuthenticatedSessionResponse,
  CurrentUserResponse,
} from '../../../core/models/auth-response.model';

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  captcha: string;
  transport?: 'browser';
}

export interface SignupRequest {
  email: string;
  password: string;
  usertype: SignupRole;
  captcha: string;
}

export interface ForgotPasswordRequest {
  email: string;
  captcha: string;
}

export interface VerificationChallengeResponse {
  Challenge: string;
  ExpiresAtUtc: string;
}

export type SignupRole = 'participant' | 'organizer' | 'volunteer';

export interface OAuthRoleSelectionResponse {
  RequiresRoleSelection: true;
  SignupToken: string;
  Email: string;
  Name: string;
  Provider: string;
  Auth?: undefined;
}

export interface OAuthAuthenticatedResponse {
  RequiresRoleSelection: false;
  Auth: AuthenticatedSessionResponse;
  SignupToken?: undefined;
  Email?: undefined;
  Name?: undefined;
  Provider?: undefined;
}

export type OAuthAuthResponse = OAuthRoleSelectionResponse | OAuthAuthenticatedResponse;

export interface ApiEnvelope<T> {
  message?: string;
  Message?: string;
  data?: T;
  Data?: T;
}

export const PendingOAuthSignupStorageKey = 'pending_oauth_signup';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly baseUrl = `${environment.backendUrl}/auth`;

  constructor(
    private http: HttpClient,
    private authToken: AuthTokenService,
  ) {}

  login(payload: LoginRequest): Observable<AuthenticatedSessionResponse> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(`${this.baseUrl}/login`, {
      ...payload,
      transport: 'browser' as const,
    }).pipe(
      map((res) => this.requireData(res, 'Login response was incomplete.')),
    );
  }

  signup(payload: SignupRequest): Observable<ApiEnvelope<VerificationChallengeResponse>> {
    return this.postWithCsrf<ApiEnvelope<VerificationChallengeResponse>>(
      `${this.baseUrl}/signup`,
      payload,
    );
  }

  verifyEmail(token: string): Observable<ApiEnvelope<AuthenticatedSessionResponse>> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(`${this.baseUrl}/verify`, {
      token,
      transport: 'browser' as const,
    });
  }

  verifyDevice(token: string): Observable<ApiEnvelope<AuthenticatedSessionResponse>> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(`${this.baseUrl}/device/verify`, {
      token,
      transport: 'browser' as const,
    });
  }

  googleVerify(idToken: string, nonce: string): Observable<OAuthAuthResponse> {
    return this.postWithCsrf<ApiEnvelope<OAuthAuthResponse>>(`${this.baseUrl}/google`, {
      token: idToken,
      nonce,
      transport: 'browser' as const,
    }).pipe(map((res) => this.requireData(res, 'Google login response was incomplete.')));
  }

  microsoftVerify(idToken: string, nonce: string): Observable<OAuthAuthResponse> {
    return this.postWithCsrf<ApiEnvelope<OAuthAuthResponse>>(`${this.baseUrl}/microsoft`, {
      token: idToken,
      nonce,
      transport: 'browser' as const,
    }).pipe(map((res) => this.requireData(res, 'Microsoft login response was incomplete.')));
  }

  completeOAuthSignup(signupToken: string, usertype: SignupRole): Observable<AuthenticatedSessionResponse> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(`${this.baseUrl}/oauth/complete`, {
      signupToken,
      usertype,
      transport: 'browser' as const,
    }).pipe(map((res) => this.requireData(res, 'OAuth signup completion response was incomplete.')));
  }

  me(): Observable<ApiEnvelope<CurrentUserResponse>> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() => this.http.get<ApiEnvelope<CurrentUserResponse>>(`${this.baseUrl}/me`, {
        withCredentials: true,
      })),
    );
  }

  logout(): Observable<void> {
    return this.postWithCsrf<void>(`${this.baseUrl}/logout`, {});
  }

  forgotPassword(payload: ForgotPasswordRequest): Observable<ApiEnvelope<VerificationChallengeResponse>> {
    return this.postWithCsrf<ApiEnvelope<VerificationChallengeResponse>>(
      `${this.baseUrl}/forgot-password`,
      payload,
    );
  }

  changePassword(password: string, token?: string): Observable<void> {
    const query = token ? `?token=${encodeURIComponent(token)}` : '';
    return this.postWithCsrf<void>(`${this.baseUrl}/change-password${query}`, {
      password,
    });
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

  private requireData<T>(response: ApiEnvelope<T>, fallbackMessage: string): T {
    const data = response.data ?? response.Data;
    if (!data) {
      throw new Error(fallbackMessage);
    }

    return data;
  }
}
