import { AbstractControl, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';

/**
 * The client-side mirror of the server's `RecoveryCodeFormat`
 * (`backend/src/main/features/auth/mfa/recovery/RecoveryCodeFormat.cs`). The two must be changed
 * together: this only decides what a form will submit, and the server re-reads every code.
 *
 * Codes are 28 Crockford base32 characters shown as seven groups of four. Entry is forgiving:
 * case, spaces, and hyphens are ignored, and O, I, and L read as the digits they resemble.
 */

export const RECOVERY_CODE_LENGTH = 28;

/** A code from an authenticator app, a text message, or an email. */
export const SIX_DIGIT_CODE = /^\d{6}$/;

/** Longest input the form accepts, leaving room for separators and stray spaces. */
export const RECOVERY_CODE_MAX_INPUT_LENGTH = 64;

export const RECOVERY_CODE_HINT =
  'Enter one of your saved recovery codes, for example ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-2345.';

const ALPHABET = /^[0-9A-HJKMNP-TV-Z]+$/;

/** The code in the server's canonical form, or null if the input cannot be a code. */
export function normalizeRecoveryCode(value: string | null | undefined): string | null {
  if (!value || value.length > RECOVERY_CODE_MAX_INPUT_LENGTH) {
    return null;
  }

  const normalized = value
    .replace(/[\s-]/g, '')
    .toUpperCase()
    .replace(/O/g, '0')
    .replace(/[IL]/g, '1');

  return normalized.length === RECOVERY_CODE_LENGTH && ALPHABET.test(normalized)
    ? normalized
    : null;
}

/** Flags `recoveryCode` unless the value can be a recovery code. Leaves emptiness to `required`. */
export const recoveryCodeValidator: ValidatorFn = (
  control: AbstractControl<string | null>,
): ValidationErrors | null => {
  const value = control.value;
  if (!value) {
    return null;
  }

  return normalizeRecoveryCode(value) ? null : { recoveryCode: true };
};

/**
 * The rules for a verification-code field. A recovery code is not six digits, so forms that offer
 * several methods switch rules with the selected method.
 */
export function verificationCodeValidators(method: string | null): ValidatorFn[] {
  return method === 'recovery_code'
    ? [Validators.required, recoveryCodeValidator]
    : [Validators.required, Validators.pattern(SIX_DIGIT_CODE)];
}
