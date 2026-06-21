import { HttpErrorResponse } from '@angular/common/http';

import {
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
});
