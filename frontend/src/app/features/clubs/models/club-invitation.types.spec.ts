import {
  normalizeClubInvitation,
  normalizeClubInvitationDecision,
  normalizeClubInvitationResolve,
} from './club-invitation.types';

describe('normalizeClubInvitation', () => {
  it('reads a PascalCase payload', () => {
    expect(
      normalizeClubInvitation({
        ClubId: 3,
        RecipientUserId: 8,
        RecipientEmail: 'staff@example.com',
        Role: 'Volunteer',
        CreatedAtUtc: '2026-01-01T00:00:00Z',
        ExpiresAtUtc: '2026-01-08T00:00:00Z',
      }),
    ).toEqual({
      clubId: 3,
      recipientUserId: 8,
      recipientEmail: 'staff@example.com',
      role: 'Volunteer',
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-08T00:00:00Z',
    });
  });

  it('defaults an empty payload to a Manager invitation', () => {
    expect(normalizeClubInvitation({})).toEqual({
      clubId: 0,
      recipientUserId: 0,
      recipientEmail: '',
      role: 'Manager',
      createdAtUtc: '',
      expiresAtUtc: '',
    });
  });

  it('treats any unrecognised role as Manager', () => {
    expect(normalizeClubInvitation({ Role: 'Owner' }).role).toBe('Manager');
  });
});

describe('camelCase precedence', () => {
  it('prefers the camelCase key on an invitation', () => {
    expect(
      normalizeClubInvitation({
        clubId: 3,
        ClubId: 99,
        recipientUserId: 8,
        recipientEmail: 'staff@example.com',
        role: 'Volunteer',
        createdAtUtc: '2026-01-01T00:00:00Z',
        expiresAtUtc: '2026-01-08T00:00:00Z',
      }),
    ).toEqual(jasmine.objectContaining({ clubId: 3, recipientUserId: 8, role: 'Volunteer' }));
  });

  it('prefers the camelCase key on a resolve payload and its nested club', () => {
    const result = normalizeClubInvitationResolve({
      state: 'Pending',
      requiresAuthentication: true,
      canAccept: true,
      canDecline: true,
      role: 'Manager',
      expiresAtUtc: '2026-01-08T00:00:00Z',
      club: { id: 3, name: 'Camel', clubImage: 'c.png' },
    });

    expect(result.state).toBe('Pending');
    expect(result.requiresAuthentication).toBeTrue();
    expect(result.club).toEqual({ id: 3, name: 'Camel', clubImage: 'c.png' });
  });

  it('defaults a nested club that carries nothing', () => {
    expect(normalizeClubInvitationResolve({ Club: {} }).club).toEqual({
      id: 0,
      name: '',
      clubImage: '',
    });
  });
});

describe('normalizeClubInvitationResolve', () => {
  it('normalizes the nested club summary', () => {
    const result = normalizeClubInvitationResolve({
      State: 'Pending',
      RequiresAuthentication: true,
      CanAccept: true,
      CanDecline: true,
      Role: 'Volunteer',
      ExpiresAtUtc: '2026-01-08T00:00:00Z',
      Club: { Id: 3, Name: 'Robotics Club', ClubImage: 'https://example.com/c.png' },
    });

    expect(result.state).toBe('Pending');
    expect(result.requiresAuthentication).toBeTrue();
    expect(result.role).toBe('Volunteer');
    expect(result.club).toEqual({
      id: 3,
      name: 'Robotics Club',
      clubImage: 'https://example.com/c.png',
    });
  });

  it('nulls the role and club when the payload omits them', () => {
    const result = normalizeClubInvitationResolve({ State: 'Expired' });

    expect(result.role).toBeNull();
    expect(result.club).toBeNull();
    expect(result.expiresAtUtc).toBeNull();
    expect(result.canAccept).toBeFalse();
    expect(result.canDecline).toBeFalse();
  });
});

describe('normalizeClubInvitationDecision', () => {
  it('reads both casings', () => {
    expect(
      normalizeClubInvitationDecision({ ClubId: 3, Role: 'Volunteer', Accepted: true }),
    ).toEqual({ clubId: 3, role: 'Volunteer', accepted: true });

    expect(normalizeClubInvitationDecision({ clubId: 4, accepted: false })).toEqual({
      clubId: 4,
      role: 'Manager',
      accepted: false,
    });
  });
});
