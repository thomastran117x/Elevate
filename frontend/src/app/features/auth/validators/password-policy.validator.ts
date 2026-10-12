import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * The client-side mirror of the length part of the server's password policy
 * (`backend/src/main/features/auth/password/PasswordPolicyOptions.cs`, `Auth:PasswordPolicy`).
 * Change the two together. The server re-checks every value and also rejects common, breached and
 * account-derived passwords, which only it can judge.
 *
 * There are deliberately no composition rules: spaces, Unicode, pasted and generated values are all
 * fine. The rules apply only to passwords being chosen, never to sign-in.
 */

export const PASSWORD_MIN_LENGTH = 15;
export const PASSWORD_MAX_LENGTH = 128;

/** Shown beside every new-password field, before anything has gone wrong. */
export const PASSWORD_POLICY_HINT =
  `Use ${PASSWORD_MIN_LENGTH}–${PASSWORD_MAX_LENGTH} characters. Spaces, emoji, and pasted or ` +
  'generated passwords are all fine — a few unrelated words make a strong passphrase. Avoid your ' +
  'username, email, or the site name.';

/** Mirrors `PasswordPolicyMessages.Length`, so the form and the API say the same thing. */
export const PASSWORD_LENGTH_MESSAGE = `Use between ${PASSWORD_MIN_LENGTH} and ${PASSWORD_MAX_LENGTH} characters.`;

/**
 * The length a person would count, as the server counts it: Unicode code points after NFKC
 * normalization, so an emoji or an accented letter is one character rather than two UTF-16 units.
 */
export function passwordLength(value: string): number {
  return [...(value ?? '').normalize('NFKC')].length;
}

/**
 * Reports `{ passwordLength: { min, max, actual } }` for a value outside the allowed range.
 *
 * Validators.required owns an empty field, so this stays silent for one rather than stacking a
 * second message on top.
 */
export const passwordPolicyValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value: string = control.value ?? '';
  if (value.length === 0) {
    return null;
  }

  const actual = passwordLength(value);
  if (actual < PASSWORD_MIN_LENGTH || actual > PASSWORD_MAX_LENGTH) {
    return { passwordLength: { min: PASSWORD_MIN_LENGTH, max: PASSWORD_MAX_LENGTH, actual } };
  }

  return null;
};
