import {
  normalizeClubInvitationLink,
  normalizeClubMemberInvitation,
  normalizeClubMemberInvitationDecision,
  normalizeClubMemberInvitationResolve,
} from './club-member-invitation.types';

describe('normalizeClubMemberInvitation', () => {
  it('reads a PascalCase payload', () => {
    expect(
      normalizeClubMemberInvitation({
        ClubId: 2,
        RecipientUserId: 5,
        RecipientEmail: 'member@example.com',
        CreatedAtUtc: '2026-01-01T00:00:00Z',
        ExpiresAtUtc: '2026-01-08T00:00:00Z',
      }),
    ).toEqual({
      clubId: 2,
      recipientUserId: 5,
      recipientEmail: 'member@example.com',
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-08T00:00:00Z',
    });
  });

  it('defaults an empty payload', () => {
    expect(normalizeClubMemberInvitation({})).toEqual({
      clubId: 0,
      recipientUserId: 0,
      recipientEmail: '',
      createdAtUtc: '',
      expiresAtUtc: '',
    });
  });
});

describe('normalizeClubInvitationLink', () => {
  it('reads a PascalCase payload', () => {
    expect(
      normalizeClubInvitationLink({
        Id: 11,
        ClubId: 2,
        ShareUrl: 'https://example.com/clubs/member-invite?token=abc',
        ExpiresAt: '2026-02-01T00:00:00Z',
        MaxRedemptions: 25,
        RedemptionCount: 4,
        IsRevoked: false,
        RevokedAtUtc: null,
        CreatedAt: '2026-01-01T00:00:00Z',
        UpdatedAt: '2026-01-02T00:00:00Z',
      }),
    ).toEqual({
      id: 11,
      clubId: 2,
      shareUrl: 'https://example.com/clubs/member-invite?token=abc',
      expiresAt: '2026-02-01T00:00:00Z',
      maxRedemptions: 25,
      redemptionCount: 4,
      isRevoked: false,
      revokedAtUtc: null,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-02T00:00:00Z',
    });
  });

  it('nulls an absent share URL and redemption cap rather than dropping the keys', () => {
    const result = normalizeClubInvitationLink({ Id: 1 });

    expect(result.shareUrl).toBeNull();
    expect(result.maxRedemptions).toBeNull();
    expect(result.redemptionCount).toBe(0);
    expect(result.isRevoked).toBeFalse();
  });
});

describe('camelCase precedence', () => {
  it('prefers the camelCase key on an invitation and a link', () => {
    expect(
      normalizeClubMemberInvitation({
        clubId: 2,
        ClubId: 99,
        recipientUserId: 5,
        recipientEmail: 'member@example.com',
        createdAtUtc: 'a',
        expiresAtUtc: 'b',
      }),
    ).toEqual(jasmine.objectContaining({ clubId: 2, recipientUserId: 5 }));

    expect(
      normalizeClubInvitationLink({
        id: 11,
        clubId: 2,
        shareUrl: 'https://e/x',
        expiresAt: 'a',
        maxRedemptions: 25,
        redemptionCount: 4,
        isRevoked: true,
        revokedAtUtc: 'b',
        createdAt: 'c',
        updatedAt: 'd',
      }),
    ).toEqual(
      jasmine.objectContaining({
        id: 11,
        shareUrl: 'https://e/x',
        maxRedemptions: 25,
        isRevoked: true,
      }),
    );
  });

  it('prefers the camelCase key on a resolve payload and its nested club', () => {
    const result = normalizeClubMemberInvitationResolve({
      state: 'Pending',
      source: 'DirectInvite',
      requiresAuthentication: true,
      canAccept: true,
      canDecline: true,
      expiresAtUtc: 'a',
      club: { id: 2, name: 'Camel', clubImage: 'c.png' },
    });

    expect(result.source).toBe('DirectInvite');
    expect(result.canDecline).toBeTrue();
    expect(result.club).toEqual({ id: 2, name: 'Camel', clubImage: 'c.png' });
  });

  it('defaults a nested club that carries nothing', () => {
    expect(normalizeClubMemberInvitationResolve({ Club: {} }).club).toEqual({
      id: 0,
      name: '',
      clubImage: '',
    });
  });
});

describe('normalizeClubMemberInvitationResolve', () => {
  it('normalizes the nested club summary and carries the source through', () => {
    const result = normalizeClubMemberInvitationResolve({
      State: 'Pending',
      Source: 'Link',
      CanAccept: true,
      Club: { Id: 2, Name: 'Robotics Club', ClubImage: '' },
    });

    expect(result.source).toBe('Link');
    expect(result.canAccept).toBeTrue();
    expect(result.club).toEqual({ id: 2, name: 'Robotics Club', clubImage: '' });
  });

  it('nulls the club when the payload omits it', () => {
    const result = normalizeClubMemberInvitationResolve({ State: 'Revoked' });

    expect(result.club).toBeNull();
    expect(result.expiresAtUtc).toBeNull();
    expect(result.requiresAuthentication).toBeFalse();
  });
});

describe('normalizeClubMemberInvitationDecision', () => {
  it('reads both casings', () => {
    expect(normalizeClubMemberInvitationDecision({ ClubId: 2, Accepted: true })).toEqual({
      clubId: 2,
      accepted: true,
    });

    expect(normalizeClubMemberInvitationDecision({})).toEqual({ clubId: 0, accepted: false });
  });
});
