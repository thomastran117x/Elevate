import { Injectable } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  AuthenticatedSessionResponse,
  CurrentUserResponse,
} from '../../../core/models/auth-response.model';

export type SignupRole = 'participant' | 'organizer' | 'volunteer';
export type LoginStepUpMethod = 'sms' | 'email' | 'totp';

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  captcha: string;
  transport?: 'browser';
  returnUrl?: string;
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

export interface EmailMfaSettings {
  maskedEmail: string;
  isEnabled: true;
}

export interface SmsMfaSettings {
  enrollmentAvailable: boolean;
  isConfigured: boolean;
  isEnabled: boolean;
  maskedPhoneNumber?: string | null;
  phoneVerifiedAtUtc?: string | null;
  canEnroll: boolean;
  canEnable: boolean;
  canDisable: boolean;
  canRemove: boolean;
}

export interface TotpMfaSettings {
  enrollmentAvailable: boolean;
  isConfigured: boolean;
  isEnabled: boolean;
  enrolledAtUtc?: string | null;
  disabledAtUtc?: string | null;
  canEnroll: boolean;
  canEnable: boolean;
  canDisable: boolean;
  canRemove: boolean;
}

export interface MfaSettingsResponse {
  email: EmailMfaSettings;
  sms: SmsMfaSettings;
  totp: TotpMfaSettings;
}

export interface MfaChallengeResponse {
  Challenge: string;
  ExpiresAtUtc: string;
  Channel: string;
  MaskedDestination: string;
}

export interface TotpEnrollmentStartResponse {
  SecretKey: string;
  QrCodeUri: string;
  ExpiresAtUtc: string;
}

export interface LoginStepUpChallengeResponse {
  Challenge: string;
  ExpiresAtUtc: string;
  AvailableMethods: LoginStepUpMethod[];
  MaskedPhone?: string | null;
  MaskedEmail: string;
}

export interface StartLoginStepUpResponse {
  Challenge: string;
  ExpiresAtUtc: string;
  SelectedMethod: LoginStepUpMethod;
  MaskedDestination: string;
  CooldownEndsAtUtc: string;
  AvailableMethods: LoginStepUpMethod[];
  MaskedPhone?: string | null;
  MaskedEmail: string;
}

export interface OAuthRoleSelectionPayload {
  SignupToken: string;
  Email: string;
  Name: string;
  Provider: string;
}

export interface LoginAuthenticatedResponse {
  Type: 'authenticated';
  Auth: AuthenticatedSessionResponse;
  StepUp?: undefined;
}

export interface LoginRequiresStepUpResponse {
  Type: 'requires_step_up';
  Auth?: undefined;
  StepUp: LoginStepUpChallengeResponse;
}

export type LoginAuthenticationResponse = LoginAuthenticatedResponse | LoginRequiresStepUpResponse;

export interface OAuthAuthenticatedResponse {
  Type: 'authenticated';
  Auth: AuthenticatedSessionResponse;
  StepUp?: undefined;
  RoleSelection?: undefined;
}

export interface OAuthRequiresStepUpResponse {
  Type: 'requires_step_up';
  Auth?: undefined;
  StepUp: LoginStepUpChallengeResponse;
  RoleSelection?: undefined;
}

export interface OAuthRequiresRoleSelectionResponse {
  Type: 'requires_role_selection';
  Auth?: undefined;
  StepUp?: undefined;
  RoleSelection: OAuthRoleSelectionPayload;
}

export type OAuthAuthenticationResponse =
  | OAuthAuthenticatedResponse
  | OAuthRequiresStepUpResponse
  | OAuthRequiresRoleSelectionResponse;

export const PendingOAuthSignupStorageKey = 'pending_oauth_signup';
export const PendingLoginStepUpStorageKey = 'pending_login_step_up';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly baseUrl = `${environment.backendUrl}/auth`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
  ) {}

  login(payload: LoginRequest): Observable<LoginAuthenticationResponse> {
    return this.postWithCsrf<ApiEnvelope<LoginAuthenticationResponse>>(`${this.baseUrl}/login`, {
      ...payload,
      transport: 'browser' as const,
    }).pipe(map((res) => this.requireData(res, 'Login response was incomplete.')));
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

  verifyDevice(token: string): Observable<AuthenticatedSessionResponse> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(
      `${this.baseUrl}/device/verify`,
      {
        token,
        transport: 'browser' as const,
      },
    ).pipe(map((res) => this.requireData(res, 'Device verification response was incomplete.')));
  }

  startLoginStepUp(
    challenge: string,
    method: LoginStepUpMethod,
  ): Observable<StartLoginStepUpResponse> {
    return this.postWithCsrf<ApiEnvelope<StartLoginStepUpResponse>>(`${this.baseUrl}/mfa/start`, {
      challenge,
      method,
    }).pipe(
      map((res) => this.requireData(res, 'Sign-in verification delivery response was incomplete.')),
    );
  }

  verifyLoginStepUp(challenge: string, code: string): Observable<AuthenticatedSessionResponse> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(
      `${this.baseUrl}/mfa/verify`,
      {
        challenge,
        code,
      },
    ).pipe(map((res) => this.requireData(res, 'Sign-in verification response was incomplete.')));
  }

  verifyTotpLoginStepUp(challenge: string, code: string): Observable<AuthenticatedSessionResponse> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(
      `${this.baseUrl}/mfa/verify/totp`,
      {
        challenge,
        code,
      },
    ).pipe(
      map((res) => this.requireData(res, 'Authenticator verification response was incomplete.')),
    );
  }

  googleVerify(
    idToken: string,
    nonce: string,
    returnUrl?: string,
  ): Observable<OAuthAuthenticationResponse> {
    return this.postWithCsrf<ApiEnvelope<OAuthAuthenticationResponse>>(`${this.baseUrl}/google`, {
      token: idToken,
      nonce,
      returnUrl,
      transport: 'browser' as const,
    }).pipe(map((res) => this.requireData(res, 'Google login response was incomplete.')));
  }

  googleCodeVerify(
    code: string,
    codeVerifier: string,
    redirectUri: string,
    nonce: string,
    returnUrl?: string,
  ): Observable<OAuthAuthenticationResponse> {
    return this.postWithCsrf<ApiEnvelope<OAuthAuthenticationResponse>>(
      `${this.baseUrl}/google/code`,
      {
        code,
        codeVerifier,
        redirectUri,
        nonce,
        returnUrl,
        transport: 'browser' as const,
      },
    ).pipe(map((res) => this.requireData(res, 'Google login response was incomplete.')));
  }

  microsoftVerify(
    idToken: string,
    nonce: string,
    returnUrl?: string,
  ): Observable<OAuthAuthenticationResponse> {
    return this.postWithCsrf<ApiEnvelope<OAuthAuthenticationResponse>>(
      `${this.baseUrl}/microsoft`,
      {
        token: idToken,
        nonce,
        returnUrl,
        transport: 'browser' as const,
      },
    ).pipe(map((res) => this.requireData(res, 'Microsoft login response was incomplete.')));
  }

  completeOAuthSignup(
    signupToken: string,
    usertype: SignupRole,
  ): Observable<AuthenticatedSessionResponse> {
    return this.postWithCsrf<ApiEnvelope<AuthenticatedSessionResponse>>(
      `${this.baseUrl}/oauth/complete`,
      {
        signupToken,
        usertype,
        transport: 'browser' as const,
      },
    ).pipe(map((res) => this.requireData(res, 'OAuth signup completion response was incomplete.')));
  }

  me(): Observable<ApiEnvelope<CurrentUserResponse>> {
    return this.getWithCsrf<ApiEnvelope<CurrentUserResponse>>(`${this.baseUrl}/me`);
  }

  getMfaStatus(): Observable<MfaSettingsResponse> {
    return this.getWithCsrf<ApiEnvelope<MfaSettingsResponse>>(`${this.baseUrl}/mfa`).pipe(
      map((res) => this.requireData(res, 'MFA settings response was incomplete.')),
    );
  }

  startMfaEnrollment(phoneNumber: string): Observable<MfaChallengeResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaChallengeResponse>>(
      `${this.baseUrl}/mfa/sms/enroll/start`,
      {
        phoneNumber,
      },
    ).pipe(map((res) => this.requireData(res, 'SMS MFA challenge response was incomplete.')));
  }

  startMfaEnable(): Observable<MfaChallengeResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaChallengeResponse>>(
      `${this.baseUrl}/mfa/sms/enable/start`,
      {},
    ).pipe(map((res) => this.requireData(res, 'SMS MFA enable response was incomplete.')));
  }

  verifyMfaEnrollment(code: string, challenge: string): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(
      `${this.baseUrl}/mfa/sms/enroll/verify`,
      {
        code,
        challenge,
      },
    ).pipe(map((res) => this.requireData(res, 'SMS MFA verification response was incomplete.')));
  }

  disableMfa(): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(
      `${this.baseUrl}/mfa/sms/disable`,
      {},
    ).pipe(map((res) => this.requireData(res, 'SMS MFA disable response was incomplete.')));
  }

  removeMfa(): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(
      `${this.baseUrl}/mfa/sms/remove`,
      {},
    ).pipe(map((res) => this.requireData(res, 'SMS MFA remove response was incomplete.')));
  }

  startTotpEnrollment(): Observable<TotpEnrollmentStartResponse> {
    return this.postWithCsrf<ApiEnvelope<TotpEnrollmentStartResponse>>(
      `${this.baseUrl}/mfa/totp/enroll/start`,
      {},
    ).pipe(map((res) => this.requireData(res, 'TOTP enrollment response was incomplete.')));
  }

  verifyTotpEnrollment(code: string): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(
      `${this.baseUrl}/mfa/totp/enroll/verify`,
      { code },
    ).pipe(map((res) => this.requireData(res, 'TOTP verification response was incomplete.')));
  }

  enableTotp(code: string): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(`${this.baseUrl}/mfa/totp/enable`, {
      code,
    }).pipe(map((res) => this.requireData(res, 'TOTP enable response was incomplete.')));
  }

  disableTotp(code: string): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(`${this.baseUrl}/mfa/totp/disable`, {
      code,
    }).pipe(map((res) => this.requireData(res, 'TOTP disable response was incomplete.')));
  }

  removeTotp(code: string): Observable<MfaSettingsResponse> {
    return this.postWithCsrf<ApiEnvelope<MfaSettingsResponse>>(`${this.baseUrl}/mfa/totp/remove`, {
      code,
    }).pipe(map((res) => this.requireData(res, 'TOTP remove response was incomplete.')));
  }

  logout(): Observable<void> {
    return this.postWithCsrf<void>(`${this.baseUrl}/logout`, {});
  }

  forgotPassword(
    payload: ForgotPasswordRequest,
  ): Observable<ApiEnvelope<VerificationChallengeResponse>> {
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

  private getWithCsrf<T>(url: string): Observable<T> {
    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() =>
        this.api.get<T>(url, {
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

  private requireData<T>(response: ApiEnvelope<T>, fallbackMessage: string): T {
    return requireEnvelopeData(response, fallbackMessage);
  }
}
