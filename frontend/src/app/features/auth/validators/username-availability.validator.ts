import { AbstractControl, AsyncValidatorFn, ValidationErrors } from '@angular/forms';
import { Observable, of, timer } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

/** Matches the server-side UsernamePolicy, so the check runs on the value the API will evaluate. */
export function normalizeUsername(value: string): string {
  return (value ?? '').trim().toLowerCase();
}

const DEBOUNCE_MS = 400;

/**
 * Reports `{ usernameTaken: true }` when the API says the name is already spoken for.
 *
 * The check is advisory: it does not reserve the name, and signup can still come back with
 * USERNAME_TAKEN if someone claims it in between. A failed or unreachable request resolves to
 * "no error" rather than blocking submission — the server rejects a duplicate regardless, so
 * failing open here costs a late error message instead of a form nobody can submit.
 */
export function usernameAvailabilityValidator(auth: AuthService): AsyncValidatorFn {
  return (control: AbstractControl): Observable<ValidationErrors | null> => {
    const username = normalizeUsername(control.value);

    // Let the synchronous validators own these cases; probing the API would only add noise.
    if (!username || username.length > 50) {
      return of(null);
    }

    // timer + switchMap debounces per keystroke: Angular resubscribes on every change, which
    // cancels the pending timer before the request is ever issued.
    return timer(DEBOUNCE_MS).pipe(
      switchMap(() => auth.checkUsernameAvailability(username)),
      map((result) => (result.available ? null : { usernameTaken: true })),
      catchError(() => of(null)),
    );
  };
}
