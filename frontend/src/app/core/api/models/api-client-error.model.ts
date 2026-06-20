export const NETWORK_API_ERROR_MESSAGE =
  "We couldn't reach EventXperience. Check your internet connection and try again.";
export const TEMPORARY_SERVER_API_ERROR_MESSAGE =
  'EventXperience is temporarily unavailable. Please try again in a few minutes.';
export const GENERIC_API_ERROR_MESSAGE =
  'Something went wrong on our side. Please try again in a moment.';

const TEMPORARY_SERVER_STATUSES = new Set([502, 503, 504]);

type ApiClientErrorKind = 'client' | 'server';

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

export function getApiClientMessage(error: unknown, fallback: string): string {
  if (isApiClientError(error)) {
    return error.message;
  }

  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return fallback;
}
