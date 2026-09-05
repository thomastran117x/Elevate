import { fakeAsync, tick } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { Observable, of, throwError } from 'rxjs';

import { AuthService, UsernameAvailabilityResponse } from '../services/auth.service';
import {
  normalizeUsername,
  usernameAvailabilityValidator,
} from './username-availability.validator';

describe('usernameAvailabilityValidator', () => {
  function createAuth(
    result: Observable<UsernameAvailabilityResponse>,
  ): jasmine.SpyObj<AuthService> {
    const auth = jasmine.createSpyObj<AuthService>('AuthService', ['checkUsernameAvailability']);
    auth.checkUsernameAvailability.and.returnValue(result);
    return auth;
  }

  function runValidator(auth: jasmine.SpyObj<AuthService>, value: string): { errors: unknown } {
    const control = new FormControl(value);
    const captured: { errors: unknown } = { errors: undefined };

    (usernameAvailabilityValidator(auth)(control) as Observable<unknown>).subscribe((errors) => {
      captured.errors = errors;
    });

    return captured;
  }

  it('reports no error when the username is available', fakeAsync(() => {
    const auth = createAuth(of({ username: 'ada', available: true }));

    const result = runValidator(auth, 'ada');
    tick(400);

    expect(result.errors).toBeNull();
    expect(auth.checkUsernameAvailability).toHaveBeenCalledWith('ada');
  }));

  it('reports usernameTaken when the name is already claimed', fakeAsync(() => {
    const auth = createAuth(of({ username: 'ada', available: false }));

    const result = runValidator(auth, 'ada');
    tick(400);

    expect(result.errors).toEqual({ usernameTaken: true });
  }));

  it('normalizes the value before asking the API', fakeAsync(() => {
    const auth = createAuth(of({ username: 'ada', available: true }));

    runValidator(auth, '  AdaLovelace  ');
    tick(400);

    expect(auth.checkUsernameAvailability).toHaveBeenCalledWith('adalovelace');
  }));

  it('does not call the API before the debounce elapses', fakeAsync(() => {
    const auth = createAuth(of({ username: 'ada', available: true }));

    runValidator(auth, 'ada');
    tick(399);
    expect(auth.checkUsernameAvailability).not.toHaveBeenCalled();

    tick(1);
    expect(auth.checkUsernameAvailability).toHaveBeenCalledTimes(1);
  }));

  it('skips the API for values the synchronous validators already reject', fakeAsync(() => {
    const auth = createAuth(of({ username: 'ada', available: true }));

    const empty = runValidator(auth, '   ');
    const tooLong = runValidator(auth, 'a'.repeat(51));
    tick(400);

    expect(empty.errors).toBeNull();
    expect(tooLong.errors).toBeNull();
    expect(auth.checkUsernameAvailability).not.toHaveBeenCalled();
  }));

  // The server rejects duplicates regardless, so failing open costs a late error message rather
  // than a form the user cannot submit.
  it('fails open when the request errors', fakeAsync(() => {
    const auth = createAuth(throwError(() => new Error('network down')));

    const result = runValidator(auth, 'ada');
    tick(400);

    expect(result.errors).toBeNull();
  }));
});

describe('normalizeUsername', () => {
  it('trims and lowercases to match the server-side policy', () => {
    expect(normalizeUsername('  AdaLovelace ')).toBe('adalovelace');
  });

  it('treats a missing value as empty', () => {
    expect(normalizeUsername(undefined as unknown as string)).toBe('');
  });
});
