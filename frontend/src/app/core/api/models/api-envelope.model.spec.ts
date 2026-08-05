import { ApiEnvelope, extractEnvelopeData, requireEnvelopeData } from './api-envelope.model';

function envelopeOf<T>(overrides: Partial<ApiEnvelope<T>>): ApiEnvelope<T> {
  return {
    success: true,
    message: 'ok',
    data: null,
    error: null,
    meta: null,
    ...overrides,
  };
}

describe('extractEnvelopeData', () => {
  it('reads the camelCase data property', () => {
    expect(extractEnvelopeData(envelopeOf({ data: { id: 1 } }))).toEqual({ id: 1 });
  });

  it('falls back to the legacy PascalCase Data property', () => {
    expect(extractEnvelopeData(envelopeOf<{ id: number }>({ Data: { id: 2 } }))).toEqual({ id: 2 });
  });

  it('prefers data over Data when both are present', () => {
    const response = envelopeOf<{ id: number }>({ data: { id: 1 }, Data: { id: 2 } });

    expect(extractEnvelopeData(response)).toEqual({ id: 1 });
  });

  it('returns a bare non-enveloped payload untouched', () => {
    expect(extractEnvelopeData({ token: 'abc' })).toEqual({ token: 'abc' });
  });

  it('returns null for an envelope whose data is null', () => {
    expect(extractEnvelopeData(envelopeOf({ data: null }))).toBeNull();
  });

  it('returns null for null and undefined responses', () => {
    expect(extractEnvelopeData(null)).toBeNull();
    expect(extractEnvelopeData(undefined)).toBeNull();
  });

  it('treats a primitive response as the payload itself', () => {
    expect(extractEnvelopeData('raw-string')).toBe('raw-string');
  });
});

describe('requireEnvelopeData', () => {
  it('returns the data when present', () => {
    expect(requireEnvelopeData(envelopeOf({ data: { id: 3 } }), 'boom')).toEqual({ id: 3 });
  });

  it('accepts the legacy PascalCase Data property', () => {
    expect(requireEnvelopeData(envelopeOf<{ id: number }>({ Data: { id: 4 } }), 'boom')).toEqual({
      id: 4,
    });
  });

  it('throws with the envelope message when data is missing', () => {
    const response = envelopeOf({ data: null, message: 'Club not found.' });

    expect(() => requireEnvelopeData(response, 'fallback')).toThrowError('Club not found.');
  });

  it('falls back to the PascalCase Message before the caller fallback', () => {
    const response = envelopeOf({ data: null, message: '', Message: 'Legacy failure.' });

    expect(() => requireEnvelopeData(response, 'fallback')).toThrowError('Legacy failure.');
  });

  it('uses the caller fallback when the envelope carries no message', () => {
    const response = envelopeOf({ data: null, message: '' });

    expect(() => requireEnvelopeData(response, 'fallback')).toThrowError('fallback');
  });
});
