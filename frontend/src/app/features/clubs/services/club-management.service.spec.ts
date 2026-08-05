import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import { ClubManagementService } from './club-management.service';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { ApiClientClientError } from '../../../core/api/models/api-client-error.model';

describe('ClubManagementService', () => {
  const base = `${environment.backendUrl}/clubs`;
  let service: ClubManagementService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubManagementService, [ApiClient]));
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('clubs', () => {
    it('normalizes the managed club list', () => {
      let data: unknown;
      service.getManagedClubs().subscribe((response) => (data = response.data));

      const request = httpMock.expectOne(`${base}/managed`);
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBeTrue();
      request.flush(pascalEnvelope([{ Id: 1, Name: 'Robotics Club', Clubtype: 'Academic' }]));

      expect(data).toEqual([
        jasmine.objectContaining({ id: 1, name: 'Robotics Club', clubType: 'Academic' }),
      ]);
    });

    it('falls back to an empty list rather than null', () => {
      let data: unknown;
      service.getManagedClubs().subscribe((response) => (data = response.data));

      httpMock.expectOne(`${base}/managed`).flush(envelope(null));

      expect(data).toEqual([]);
    });

    it('creates and updates a club, normalizing the returned payload', () => {
      const payload = {
        name: 'Robotics Club',
        description: 'Build robots',
        clubtype: 'academic',
        clubImageUrl: '',
      };

      let created: unknown;
      service.createClub(payload).subscribe((response) => (created = response.data));
      const post = httpMock.expectOne(base);
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual(payload);
      post.flush(pascalEnvelope({ Id: 1, Name: 'Robotics Club' }));
      expect(created).toEqual(jasmine.objectContaining({ id: 1 }));

      service.updateClub(1, payload).subscribe();
      const put = httpMock.expectOne(`${base}/1`);
      expect(put.request.method).toBe('PUT');
      put.flush(pascalEnvelope({ Id: 1 }));
    });

    it('transfers ownership by identifier', () => {
      service.transferOwnership(1, 'jamie@example.com').subscribe();

      const request = httpMock.expectOne(`${base}/1/transfer-ownership`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ newOwnerIdentifier: 'jamie@example.com' });
      request.flush(pascalEnvelope({ Id: 1 }));
    });

    it('deletes a club', () => {
      service.deleteClub(1).subscribe();

      const request = httpMock.expectOne(`${base}/1`);
      expect(request.request.method).toBe('DELETE');
      request.flush(envelope(null));
    });
  });

  describe('members and staff', () => {
    it('pages members and only sends a non-blank search', () => {
      service.getMembers(1, 2, 5, '  jamie  ').subscribe();

      const withSearch = httpMock.expectOne((req) => req.url === `${base}/1/members`);
      expect(withSearch.request.params.get('page')).toBe('2');
      expect(withSearch.request.params.get('pageSize')).toBe('5');
      expect(withSearch.request.params.get('search')).toBe('jamie');
      withSearch.flush(envelope(null));

      service.getMembers(1, 1, 20, '   ').subscribe();
      const blankSearch = httpMock.expectOne((req) => req.url === `${base}/1/members`);
      expect(blankSearch.request.params.has('search')).toBeFalse();
      blankSearch.flush(envelope(null));
    });

    it('normalizes a PascalCase members page', () => {
      let data: unknown;
      service.getMembers(1).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url === `${base}/1/members`)
        .flush(pascalEnvelope({ Items: [{ Id: 1, Name: 'Jamie' }], TotalCount: 1 }));

      expect(data).toEqual(
        jasmine.objectContaining({
          totalCount: 1,
          items: [jasmine.objectContaining({ id: 1, name: 'Jamie' })],
        }),
      );
    });

    it('normalizes the staff list and defaults an unknown role to Manager', () => {
      let data: unknown;
      service.getStaff(1).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url === `${base}/1/staff`)
        .flush(
          pascalEnvelope([
            { Id: 1, Role: 'Volunteer' },
            { Id: 2, Role: 'Owner' },
          ]),
        );

      expect(data).toEqual([
        jasmine.objectContaining({ id: 1, role: 'Volunteer' }),
        jasmine.objectContaining({ id: 2, role: 'Manager' }),
      ]);
    });

    it('removes a staff member', () => {
      service.removeStaff(1, 9).subscribe();

      const request = httpMock.expectOne(`${base}/1/staff/9`);
      expect(request.request.method).toBe('DELETE');
      request.flush(envelope(null));
    });
  });

  describe('staff invitations', () => {
    it('sends the identifier and role, and normalizes the invitation', () => {
      let data: unknown;
      service
        .inviteStaff(1, 'jamie@example.com', 'Volunteer')
        .subscribe((response) => (data = response.data));

      const request = httpMock.expectOne(`${base}/1/staff/invitations`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        identifier: 'jamie@example.com',
        role: 'Volunteer',
      });
      request.flush(pascalEnvelope({ ClubId: 1, RecipientUserId: 9, Role: 'Volunteer' }));

      expect(data).toEqual(
        jasmine.objectContaining({ clubId: 1, recipientUserId: 9, role: 'Volunteer' }),
      );
    });

    it('lists invitations, defaulting to an empty array', () => {
      let data: unknown;
      service.getStaffInvitations(1).subscribe((response) => (data = response.data));
      httpMock.expectOne(`${base}/1/staff/invitations`).flush(envelope(null));
      expect(data).toEqual([]);

      service.getStaffInvitations(1).subscribe((response) => (data = response.data));
      httpMock
        .expectOne(`${base}/1/staff/invitations`)
        .flush(pascalEnvelope([{ ClubId: 1, RecipientEmail: 'a@example.com' }]));
      expect(data).toEqual([jasmine.objectContaining({ recipientEmail: 'a@example.com' })]);
    });

    it('revokes an invitation by recipient id', () => {
      service.revokeStaffInvitation(1, 9).subscribe();

      const request = httpMock.expectOne(`${base}/1/staff/invitations/9/revoke`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(envelope(null));
    });
  });

  describe('member invitations and links', () => {
    it('invites a member by identifier', () => {
      let data: unknown;
      service.inviteMember(1, 'jamie').subscribe((response) => (data = response.data));

      const request = httpMock.expectOne(`${base}/1/members/invitations`);
      expect(request.request.body).toEqual({ identifier: 'jamie' });
      request.flush(pascalEnvelope({ ClubId: 1, RecipientEmail: 'jamie@example.com' }));

      expect(data).toEqual(jasmine.objectContaining({ recipientEmail: 'jamie@example.com' }));
    });

    it('lists and revokes member invitations', () => {
      service.getMemberInvitations(1).subscribe();
      httpMock.expectOne(`${base}/1/members/invitations`).flush(envelope(null));

      service.revokeMemberInvitation(1, 9).subscribe();
      const revoke = httpMock.expectOne(`${base}/1/members/invitations/9/revoke`);
      expect(revoke.request.method).toBe('POST');
      revoke.flush(envelope(null));
    });

    it('omits maxRedemptions from the link body when it is null', () => {
      service.createMemberInviteLink(1, '2026-09-01T00:00:00Z', null).subscribe();

      const request = httpMock.expectOne(`${base}/1/members/invitation-links`);
      expect(request.request.body).toEqual({ expiresAt: '2026-09-01T00:00:00Z' });
      request.flush(pascalEnvelope({ Id: 1 }));
    });

    it('includes maxRedemptions when a cap is set', () => {
      let data: unknown;
      service
        .createMemberInviteLink(1, '2026-09-01T00:00:00Z', 25)
        .subscribe((response) => (data = response.data));

      const request = httpMock.expectOne(`${base}/1/members/invitation-links`);
      expect(request.request.body).toEqual({
        expiresAt: '2026-09-01T00:00:00Z',
        maxRedemptions: 25,
      });
      request.flush(pascalEnvelope({ Id: 1, MaxRedemptions: 25, ShareUrl: 'https://e/x' }));

      expect(data).toEqual(
        jasmine.objectContaining({ id: 1, maxRedemptions: 25, shareUrl: 'https://e/x' }),
      );
    });

    it('lists and revokes invite links', () => {
      let data: unknown;
      service.getMemberInviteLinks(1).subscribe((response) => (data = response.data));
      httpMock
        .expectOne(`${base}/1/members/invitation-links`)
        .flush(pascalEnvelope([{ Id: 1 }, { Id: 2 }]));
      expect((data as { id: number }[]).map((l) => l.id)).toEqual([1, 2]);

      service.revokeMemberInviteLink(1, 2).subscribe((response) => (data = response.data));
      const revoke = httpMock.expectOne(`${base}/1/members/invitation-links/2/revoke`);
      expect(revoke.request.body).toEqual({});
      revoke.flush(pascalEnvelope({ Id: 2, IsRevoked: true }));
      expect(data).toEqual(jasmine.objectContaining({ isRevoked: true }));
    });
  });

  describe('version history', () => {
    it('pages versions and normalizes the result', () => {
      let data: unknown;
      service.getVersions(1, 2, 5).subscribe((response) => (data = response.data));

      const request = httpMock.expectOne((req) => req.url === `${base}/1/versions`);
      expect(request.request.params.get('page')).toBe('2');
      expect(request.request.params.get('pageSize')).toBe('5');
      request.flush(pascalEnvelope({ Items: [{ VersionNumber: 3 }], TotalCount: 1 }));

      expect(data).toEqual(
        jasmine.objectContaining({
          items: [jasmine.objectContaining({ versionNumber: 3 })],
        }),
      );
    });

    it('normalizes a version detail including its snapshot', () => {
      let data: unknown;
      service.getVersion(1, 3).subscribe((response) => (data = response.data));

      httpMock
        .expectOne(`${base}/1/versions/3`)
        .flush(pascalEnvelope({ VersionNumber: 3, Snapshot: { Name: 'Robotics Club' } }));

      expect(data).toEqual(
        jasmine.objectContaining({
          versionNumber: 3,
          snapshot: jasmine.objectContaining({ name: 'Robotics Club' }),
        }),
      );
    });

    it('normalizes the club nested in a rollback result', () => {
      let data: unknown;
      service.rollback(1, 3).subscribe((response) => (data = response.data));

      const request = httpMock.expectOne(`${base}/1/versions/3/rollback`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(
        pascalEnvelope({
          Club: { Id: 1, Name: 'Robotics Club' },
          RestoredFromVersionNumber: 3,
          NewVersionNumber: 5,
        }),
      );

      expect(data).toEqual(
        jasmine.objectContaining({
          restoredFromVersionNumber: 3,
          newVersionNumber: 5,
          club: jasmine.objectContaining({ id: 1, name: 'Robotics Club' }),
        }),
      );
    });
  });

  describe('analytics', () => {
    it('reads analytics from the events endpoint, not the clubs one', () => {
      let data: unknown;
      service.getAnalytics(1).subscribe((response) => (data = response.data));

      const request = httpMock.expectOne(`${environment.backendUrl}/events/clubs/1/analytics`);
      expect(request.request.method).toBe('GET');
      request.flush(
        pascalEnvelope({
          ClubId: 1,
          TotalEvents: 12,
          RevenueTrend: [{ Date: '2026-01-01', Amount: 250 }],
        }),
      );

      expect(data).toEqual(
        jasmine.objectContaining({
          clubId: 1,
          totalEvents: 12,
          revenueTrend: [{ date: '2026-01-01', value: 250 }],
        }),
      );
    });
  });

  describe('camelCase and empty envelopes', () => {
    // Every method unwraps `data ?? Data ?? null`; these walk the camelCase arm and the
    // null arm that the PascalCase specs above never reach.
    const cases: Array<[string, () => void, string, 'list' | 'single']> = [
      ['managed clubs', () => service.getManagedClubs().subscribe(), `${base}/managed`, 'list'],
      ['staff', () => service.getStaff(1).subscribe(), `${base}/1/staff`, 'list'],
      [
        'staff invitations',
        () => service.getStaffInvitations(1).subscribe(),
        `${base}/1/staff/invitations`,
        'list',
      ],
      [
        'member invitations',
        () => service.getMemberInvitations(1).subscribe(),
        `${base}/1/members/invitations`,
        'list',
      ],
      [
        'invite links',
        () => service.getMemberInviteLinks(1).subscribe(),
        `${base}/1/members/invitation-links`,
        'list',
      ],
      ['members page', () => service.getMembers(1).subscribe(), `${base}/1/members`, 'single'],
      ['versions page', () => service.getVersions(1).subscribe(), `${base}/1/versions`, 'single'],
      [
        'version detail',
        () => service.getVersion(1, 3).subscribe(),
        `${base}/1/versions/3`,
        'single',
      ],
      [
        'rollback',
        () => service.rollback(1, 3).subscribe(),
        `${base}/1/versions/3/rollback`,
        'single',
      ],
      [
        'analytics',
        () => service.getAnalytics(1).subscribe(),
        `${environment.backendUrl}/events/clubs/1/analytics`,
        'single',
      ],
      [
        'invite member',
        () => service.inviteMember(1, 'jamie').subscribe(),
        `${base}/1/members/invitations`,
        'single',
      ],
      [
        'invite staff',
        () => service.inviteStaff(1, 'jamie', 'Manager').subscribe(),
        `${base}/1/staff/invitations`,
        'single',
      ],
      [
        'revoke link',
        () => service.revokeMemberInviteLink(1, 2).subscribe(),
        `${base}/1/members/invitation-links/2/revoke`,
        'single',
      ],
    ];

    for (const [label, call, url, shape] of cases) {
      it(`reads ${label} from the camelCase data key`, () => {
        call();

        httpMock
          .expectOne((req) => req.url === url)
          .flush(envelope(shape === 'list' ? [{ id: 1 }] : { id: 1 }));
      });

      it(`tolerates an empty ${label} envelope`, () => {
        call();

        httpMock.expectOne((req) => req.url === url).flush(envelope(null));
      });
    }
  });

  it('surfaces a 4xx as a typed client error', () => {
    let thrown: unknown;
    service.inviteStaff(1, 'nobody', 'Manager').subscribe({ error: (e) => (thrown = e) });

    httpMock
      .expectOne(`${base}/1/staff/invitations`)
      .flush(errorEnvelope('RESOURCE_NOT_FOUND', 'No account matches that identifier.'), {
        status: 404,
        statusText: 'Not Found',
      });

    expect(thrown).toEqual(jasmine.any(ApiClientClientError));
    expect((thrown as ApiClientClientError).code).toBe('RESOURCE_NOT_FOUND');
  });
});
