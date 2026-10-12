import { FormControl } from '@angular/forms';

import {
  PASSWORD_LENGTH_MESSAGE,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  PASSWORD_POLICY_HINT,
  passwordLength,
  passwordPolicyValidator,
} from './password-policy.validator';

describe('passwordPolicyValidator', () => {
  const validate = (value: string | null) => passwordPolicyValidator(new FormControl(value));

  it('leaves an empty value to Validators.required', () => {
    expect(validate('')).toBeNull();
    expect(validate(null)).toBeNull();
  });

  it('enforces the 15 to 128 character range at its boundaries', () => {
    expect(validate('x'.repeat(14))).toEqual({
      passwordLength: { min: 15, max: 128, actual: 14 },
    });
    expect(validate('x'.repeat(15))).toBeNull();
    expect(validate('x'.repeat(128))).toBeNull();
    expect(validate('x'.repeat(129))).toEqual({
      passwordLength: { min: 15, max: 128, actual: 129 },
    });
  });

  it('imposes no composition rules', () => {
    expect(validate('all lowercase words')).toBeNull();
    expect(validate('  spaces   count   too  ')).toBeNull();
  });

  it('counts emoji and combining marks the way the server does', () => {
    // 14 emoji are 28 UTF-16 units but only 14 characters.
    expect(validate('🔐'.repeat(14))).not.toBeNull();
    expect(validate('🔐'.repeat(15))).toBeNull();
    // "e" followed by a combining acute accent normalizes to a single "é".
    expect(passwordLength('é'.repeat(14))).toBe(14);
  });

  it('describes the rule in the same words as the API', () => {
    expect(PASSWORD_MIN_LENGTH).toBe(15);
    expect(PASSWORD_MAX_LENGTH).toBe(128);
    expect(PASSWORD_LENGTH_MESSAGE).toBe('Use between 15 and 128 characters.');
    expect(PASSWORD_POLICY_HINT).toContain('15–128 characters');
  });
});
