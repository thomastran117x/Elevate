import { HttpErrorResponse } from '@angular/common/http';

export const NETWORK_API_ERROR_MESSAGE =
  "We couldn't reach EventXperience. Check your internet connection and try again.";
export const TEMPORARY_SERVER_API_ERROR_MESSAGE =
  'EventXperience is temporarily unavailable. Please try again in a few minutes.';
export const GENERIC_API_ERROR_MESSAGE =
  'Something went wrong on our side. Please try again in a moment.';
export const DEVICE_VERIFICATION_REQUIRED_ERROR_CODE = 'DEVICE_VERIFICATION_REQUIRED';
export const DEVICE_VERIFICATION_REQUIRED_MESSAGE =
  "We sent a verification link to your email. Open it on this device and we'll finish signing you in automatically.";

const TEMPORARY_SERVER_STATUSES = new Set([502, 503, 504]);

type ApiClientErrorKind = 'client' | 'server';

type ApiErrorPayload = {
  message?: unknown;
  Message?: unknown;
  error?: { details?: unknown; Details?: unknown } | null;
};

/**
 * Flattens a backend `error.details` payload into a list of human-readable strings.
 * Handles the validation shape `{ field: string[] }`, plain arrays, and bare strings.
 */
function collectDetailStrings(details: unknown): string[] {
  if (typeof details === 'string') {
    return details.trim() ? [details.trim()] : [];
  }
  if (Array.isArray(details)) {
    return details.flatMap(collectDetailStrings);
  }
  if (details && typeof details === 'object') {
    return Object.values(details as Record<string, unknown>).flatMap(collectDetailStrings);
  }
  return [];
}

function formatDetailMessages(details: unknown): string | null {
  const messages = [...new Set(collectDetailStrings(details))];
  return messages.length ? messages.join(' ') : null;
}

class ApiClientRequestError extends Error {
  constructor(
    message: string,
    public readonly kind: ApiClientErrorKind,
    public readonly status?: number,
    public readonly code?: string,
    public readonly details?: unknown,
    public readonly originalError?: unknown,
  ) {
    super(message);
    this.name = kind === 'client' ? 'ApiClientClientError' : 'ApiClientServerError';
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

export class ApiClientClientError extends ApiClientRequestError {
  constructor(
    message: string,
    status: number,
    code?: string,
    details?: unknown,
    originalError?: unknown,
  ) {
    super(message, 'client', status, code, details, originalError);
  }
}

export class ApiClientServerError extends ApiClientRequestError {
  constructor(message: string, status?: number, originalError?: unknown) {
    super(message, 'server', status, undefined, undefined, originalError);
  }
}

export type ApiClientError = ApiClientClientError | ApiClientServerError;

export function getServerErrorMessage(status?: number): string {
  if (status === 0) {
    return NETWORK_API_ERROR_MESSAGE;
  }

  if (status !== undefined && TEMPORARY_SERVER_STATUSES.has(status)) {
    return TEMPORARY_SERVER_API_ERROR_MESSAGE;
  }

  return GENERIC_API_ERROR_MESSAGE;
}

export function isApiClientError(error: unknown): error is ApiClientError {
  return (
    typeof error === 'object' &&
    error !== null &&
    'kind' in error &&
    ((error as { kind?: unknown }).kind === 'client' ||
      (error as { kind?: unknown }).kind === 'server') &&
    'message' in error &&
    typeof (error as { message?: unknown }).message === 'string'
  );
}

export function isApiClientClientError(error: unknown): error is ApiClientClientError {
  return isApiClientError(error) && error.kind === 'client';
}

export function isApiClientErrorCode(error: unknown, code: string): boolean {
  return isApiClientClientError(error) && error.code === code;
}

export function getApiClientMessage(error: unknown, fallback: string): string {
  if (isApiClientError(error)) {
    // Prefer field-level details (e.g. validation messages) over the generic
    // top-level message like "Validation failed."
    const detailMessage = formatDetailMessages(error.details);
    if (detailMessage) {
      return detailMessage;
    }
    return error.message.trim() ? error.message : fallback;
  }

  const rawHttpMessage = getRawHttpErrorMessage(error);
  if (rawHttpMessage) {
    return rawHttpMessage;
  }

  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return fallback;
}

function getRawHttpErrorMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }

  const payload = error.error;
  if (typeof payload === 'string' && payload.trim()) {
    return payload;
  }

  if (typeof payload !== 'object' || payload === null) {
    return null;
  }

  const typed = payload as ApiErrorPayload;

  const detailMessage = formatDetailMessages(typed.error?.details ?? typed.error?.Details);
  if (detailMessage) {
    return detailMessage;
  }

  const message = typed.message;
  if (typeof message === 'string' && message.trim()) {
    return message;
  }

  const pascalMessage = typed.Message;
  if (typeof pascalMessage === 'string' && pascalMessage.trim()) {
    return pascalMessage;
  }

  return null;
}
