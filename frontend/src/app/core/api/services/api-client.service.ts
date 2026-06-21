import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  HttpHeaders,
  HttpParams,
} from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
  getServerErrorMessage,
} from '../models/api-client-error.model';

type ApiQueryParamValue = string | number | boolean | readonly (string | number | boolean)[];

type ApiRequestOptions = {
  headers?: HttpHeaders | Record<string, string | string[]>;
  context?: HttpContext;
  params?: HttpParams | Record<string, ApiQueryParamValue>;
  reportProgress?: boolean;
  responseType?: 'json';
  withCredentials?: boolean;
  transferCache?: { includeHeaders?: string[] } | boolean;
};

type ApiDeleteOptions = ApiRequestOptions & {
  body?: unknown;
};

type ApiErrorPayload = {
  message?: unknown;
  Message?: unknown;
  error?: {
    code?: unknown;
    Code?: unknown;
    details?: unknown;
    Details?: unknown;
  } | null;
};

@Injectable({ providedIn: 'root' })
export class ApiClient {
  constructor(private http: HttpClient) {}

  get<T>(url: string, options?: ApiRequestOptions): Observable<T> {
    return this.http.get<T>(url, options).pipe(catchError((error) => this.throwNormalized(error)));
  }

  post<T>(url: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.http
      .post<T>(url, body, options)
      .pipe(catchError((error) => this.throwNormalized(error)));
  }

  put<T>(url: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.http
      .put<T>(url, body, options)
      .pipe(catchError((error) => this.throwNormalized(error)));
  }

  patch<T>(url: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.http
      .patch<T>(url, body, options)
      .pipe(catchError((error) => this.throwNormalized(error)));
  }

  delete<T>(url: string, options?: ApiDeleteOptions): Observable<T> {
    return this.http
      .delete<T>(url, options)
      .pipe(catchError((error) => this.throwNormalized(error)));
  }

  private throwNormalized(error: unknown): Observable<never> {
    return throwError(() => this.normalizeError(error));
  }

  private normalizeError(error: unknown): ApiClientClientError | ApiClientServerError {
    if (!(error instanceof HttpErrorResponse)) {
      return new ApiClientServerError(GENERIC_API_ERROR_MESSAGE, undefined, error);
    }

    const status = Number.isFinite(error.status) ? error.status : undefined;

    if (status !== undefined && status >= 400 && status < 500) {
      const payload = this.readPayload(error.error);
      return new ApiClientClientError(
        this.readMessage(payload, error.message),
        status,
        this.readCode(payload),
        this.readDetails(payload),
        error,
      );
    }

    return new ApiClientServerError(getServerErrorMessage(status), status, error);
  }

  private readPayload(errorBody: unknown): ApiErrorPayload | null {
    if (typeof errorBody !== 'object' || errorBody === null) {
      return null;
    }

    return errorBody as ApiErrorPayload;
  }

  private readMessage(payload: ApiErrorPayload | null, fallback: string): string {
    const message =
      typeof payload?.message === 'string'
        ? payload.message
        : typeof payload?.Message === 'string'
          ? payload.Message
          : null;

    if (message?.trim()) {
      return message;
    }

    if (fallback.trim()) {
      return fallback;
    }

    return GENERIC_API_ERROR_MESSAGE;
  }

  private readCode(payload: ApiErrorPayload | null): string | undefined {
    const code =
      typeof payload?.error?.code === 'string'
        ? payload.error.code
        : typeof payload?.error?.Code === 'string'
          ? payload.error.Code
          : undefined;

    return code?.trim() ? code : undefined;
  }

  private readDetails(payload: ApiErrorPayload | null): unknown {
    return payload?.error?.details ?? payload?.error?.Details;
  }
}
