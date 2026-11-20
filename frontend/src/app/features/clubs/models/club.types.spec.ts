import { makeClub } from '@testing';

import { isClubStaff } from './club.types';

describe('isClubStaff', () => {
  it('is false without a club or for a plain member', () => {
    expect(isClubStaff(null)).toBeFalse();
    expect(isClubStaff(makeClub())).toBeFalse();
  });

  it('is true for the owner, any staff role, and admins (who can manage)', () => {
    expect(isClubStaff(makeClub({ isOwner: true }))).toBeTrue();
    expect(isClubStaff(makeClub({ isManager: true }))).toBeTrue();
    expect(isClubStaff(makeClub({ isVolunteer: true }))).toBeTrue();
    expect(isClubStaff(makeClub({ canManage: true }))).toBeTrue();
  });
});
