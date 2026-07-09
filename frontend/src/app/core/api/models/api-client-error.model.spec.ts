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
});
