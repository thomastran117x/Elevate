import { HttpErrorResponse } from '@angular/common/http';

import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
  getApiClientMessage,
} from './api-client-error.model';

describe('api-client-error.model', () => {
  it('prefers envelope messages from raw HttpErrorResponse objects', () => {
    const error = new HttpErrorResponse({
      status: 503,
      statusText: 'Service Unavailable',
      error: {
        message: 'Backend says try again later.',
      },
    });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Backend says try again later.');
  });

  it('supports PascalCase envelope messages on raw HttpErrorResponse objects', () => {
    const error = new HttpErrorResponse({
      status: 500,
      statusText: 'Server Error',
      error: {
        Message: 'Backend PascalCase message.',
      },
    });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Backend PascalCase message.');
  });

  it('still prefers normalized ApiClientError messages', () => {
    const error = new ApiClientServerError(GENERIC_API_ERROR_MESSAGE, 500);

    expect(getApiClientMessage(error, 'Fallback message.')).toBe(GENERIC_API_ERROR_MESSAGE);
  });

  it('surfaces validation detail messages over the generic top-level message', () => {
    const error = new ApiClientClientError('Validation failed.', 400, 'VALIDATION_ERROR', {
      Username: ['Username is required.'],
      Phone: ['Phone must be 30 characters or fewer.'],
    });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe(
      'Username is required. Phone must be 30 characters or fewer.',
    );
  });

  it('reads validation details from a raw HttpErrorResponse envelope', () => {
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: {
        message: 'Validation failed.',
        error: {
          code: 'VALIDATION_ERROR',
          details: { NewPassword: ['Password must be at least 8 characters.'] },
        },
      },
    });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe(
      'Password must be at least 8 characters.',
    );
  });

  it('reads PascalCase Details from a raw HttpErrorResponse envelope', () => {
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: { error: { Code: 'VALIDATION_ERROR', Details: ['Name is required.'] } },
    });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Name is required.');
  });

  it('returns a raw string error body as the message', () => {
    const error = new HttpErrorResponse({ status: 502, error: 'Bad gateway.' });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Bad gateway.');
  });

  it('ignores a whitespace-only string error body', () => {
    const error = new HttpErrorResponse({ status: 502, error: '   ' });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Fallback message.');
  });

  it('falls back when the error body is not an object', () => {
    const error = new HttpErrorResponse({ status: 502, error: 42 });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Fallback message.');
  });

  it('falls back when the envelope carries no usable message', () => {
    const error = new HttpErrorResponse({
      status: 500,
      error: { message: '   ', Message: '   ' },
    });

    expect(getApiClientMessage(error, 'Fallback message.')).toBe('Fallback message.');
  });

  it('uses a plain Error message when there is no envelope', () => {
    expect(getApiClientMessage(new Error('Network down.'), 'Fallback message.')).toBe(
      'Network down.',
    );
  });

  it('falls back for a blank Error message and for a non-Error throw', () => {
    expect(getApiClientMessage(new Error('   '), 'Fallback message.')).toBe('Fallback message.');
    expect(getApiClientMessage('just a string', 'Fallback message.')).toBe('Fallback message.');
    expect(getApiClientMessage(null, 'Fallback message.')).toBe('Fallback message.');
  });

  it('falls back when a typed error carries a blank message and no details', () => {
    expect(getApiClientMessage(new ApiClientClientError('   ', 400), 'Fallback message.')).toBe(
      'Fallback message.',
    );
  });

  describe('detail flattening', () => {
    function messageFor(details: unknown): string {
      return getApiClientMessage(
        new ApiClientClientError('Validation failed.', 400, 'VALIDATION_ERROR', details),
        'Fallback message.',
      );
    }

    it('accepts a bare string', () => {
      expect(messageFor('Name is required.')).toBe('Name is required.');
    });

    it('trims and drops a whitespace-only string', () => {
      expect(messageFor('  Name is required.  ')).toBe('Name is required.');
      expect(messageFor('   ')).toBe('Validation failed.');
    });

    it('flattens nested arrays', () => {
      expect(messageFor([['A.'], ['B.']])).toBe('A. B.');
    });

    it('de-duplicates repeated messages', () => {
      expect(messageFor({ a: ['Same.'], b: ['Same.'] })).toBe('Same.');
    });

    it('ignores non-string leaves', () => {
      expect(messageFor({ a: [1, true, null] })).toBe('Validation failed.');
      expect(messageFor(undefined)).toBe('Validation failed.');
    });
  });
});
