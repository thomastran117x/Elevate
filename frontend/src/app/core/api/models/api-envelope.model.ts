export interface ApiErrorResponse {
  code: string;
  details: unknown | null;
}

export interface ApiEnvelope<T, M = Record<string, unknown> | null> {
  success: boolean;
  message: string;
  data: T | null;
  error: ApiErrorResponse | null;
  meta: M | null;

  // Temporary compatibility shim during the response-contract rollout.
  Message?: string;
  Data?: T;
}

export function extractEnvelopeData<T>(response: ApiEnvelope<T> | T | null | undefined): T | null {
  if (response == null) {
    return null;
  }

  if (typeof response === 'object' && ('data' in response || 'Data' in response)) {
    const envelope = response as ApiEnvelope<T>;
    return envelope.data ?? envelope.Data ?? null;
  }

  return response as T;
}

export function requireEnvelopeData<T>(
  response: ApiEnvelope<T>,
  fallbackMessage: string,
): T {
  const data = response.data ?? response.Data;
  if (data == null) {
    throw new Error(response.message || response.Message || fallbackMessage);
  }

  return data;
}
