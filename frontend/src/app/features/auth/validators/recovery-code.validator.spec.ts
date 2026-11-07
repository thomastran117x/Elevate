import { FormControl } from '@angular/forms';

import {
  normalizeRecoveryCode,
  RECOVERY_CODE_LENGTH,
  recoveryCodeValidator,
  verificationCodeValidators,
} from './recovery-code.validator';

describe('recovery code validator', () => {
  it('normalizes case, separators, and look-alike letters like the server', () => {
    expect(normalizeRecoveryCode('abcd-efgh-jkmn-pqrs-tvwx-yz01-2345')).toBe(
      'ABCDEFGHJKMNPQRSTVWXYZ012345',
    );
    expect(normalizeRecoveryCode(' OOOO IIII LLLL oooo iiii llll 0000 ')).toBe(
      '0000111111110000111111110000',
    );
  });

  it('rejects input that cannot be a code', () => {
    for (const value of [
      '',
      null,
      undefined,
      '123456',
      'ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-234',
      'UUUU-EFGH-JKMN-PQRS-TVWX-YZ01-2345',
      'ABCD_EFGH_JKMN_PQRS_TVWX_YZ01_2345',
      '-'.repeat(60) + 'A'.repeat(RECOVERY_CODE_LENGTH),
    ]) {
      expect(normalizeRecoveryCode(value)).withContext(String(value)).toBeNull();
    }
  });

  it('flags only non-empty values that cannot be a code', () => {
    expect(recoveryCodeValidator(new FormControl(''))).toBeNull();
    expect(recoveryCodeValidator(new FormControl('ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-2345'))).toBeNull();
    expect(recoveryCodeValidator(new FormControl('123456'))).toEqual({ recoveryCode: true });
  });

  it('switches a code field between six digits and recovery codes by method', () => {
    const errorsFor = (method: string | null, value: string) =>
      new FormControl(value, verificationCodeValidators(method)).errors;
    const recovery = 'ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-2345';

    for (const method of [null, 'totp', 'sms', 'email']) {
      expect(errorsFor(method, '123456')).withContext(String(method)).toBeNull();
      expect(errorsFor(method, recovery)).withContext(String(method)).not.toBeNull();
      expect(errorsFor(method, '')).withContext(String(method)).toEqual({ required: true });
    }
    expect(errorsFor('recovery_code', recovery)).toBeNull();
    expect(errorsFor('recovery_code', '123456')).toEqual({ recoveryCode: true });
    expect(errorsFor('recovery_code', '')).toEqual({ required: true });
  });
});
