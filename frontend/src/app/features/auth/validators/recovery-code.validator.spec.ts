import { FormControl } from '@angular/forms';

import {
  normalizeRecoveryCode,
  RECOVERY_CODE_LENGTH,
  recoveryCodeValidator,
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
});
