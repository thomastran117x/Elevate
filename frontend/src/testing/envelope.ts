import { ApiEnvelope } from '../app/core/api/models/api-envelope.model';

/**
 * Builders for the `{ success, message, data, error, meta }` response contract
 * the backend wraps every payload in. Specs previously hand-wrote these inline.
 */
export function envelope<T>(
  data: T | null,
  overrides: Partial<ApiEnvelope<T>> = {},
): ApiEnvelope<T> {
  return {
    success: true,
    message: 'ok',
    data,
    error: null,
    meta: null,
    ...overrides,
  };
}

/** The legacy PascalCase shape still emitted by some endpoints. */
export function pascalEnvelope<T>(data: T): Record<string, unknown> {
  return {
    Success: true,
    Message: 'ok',
    Data: data,
    Error: null,
    Meta: null,
  };
}

export function errorEnvelope(code: string, message: string): Record<string, unknown> {
  return {
    success: false,
    message,
    data: null,
    error: { code, details: null },
    meta: null,
  };
}
