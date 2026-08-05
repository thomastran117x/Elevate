import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';

import { ClubMemberInvitationsService } from './club-member-invitations.service';

describe('ClubMemberInvitationsService', () => {
  const base = `${environment.backendUrl}/clubs/members/invitations`;
  const linkBase = `${environment.backendUrl}/clubs/members/invitation-links`;
  let service: ClubMemberInvitationsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubMemberInvitationsService));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('reports the invite source so the page can pick accept vs redeem', () => {
    let resolved: unknown;
    service.resolve('tok-1').subscribe((result) => (resolved = result));

    const request = httpMock.expectOne(`${base}/resolve`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ token: 'tok-1' });
    request.flush(
      pascalEnvelope({
        State: 'Pending',
        Source: 'Link',
        CanAccept: true,
        Club: { Id: 2, Name: 'Robotics Club', ClubImage: '' },
      }),
    );

    expect(resolved).toEqual(
      jasmine.objectContaining({
        state: 'Pending',
        source: 'Link',
        club: { id: 2, name: 'Robotics Club', clubImage: '' },
      }),
    );
  });

  for (const path of ['accept', 'decline'] as const) {
    it(`posts the token to ${path}`, () => {
      let decision: unknown;
      service[path]('tok-1').subscribe((result) => (decision = result));

      const request = httpMock.expectOne(`${base}/${path}`);
      expect(request.request.body).toEqual({ token: 'tok-1' });
      expect(request.request.withCredentials).toBeTrue();
      request.flush(pascalEnvelope({ ClubId: 2, Accepted: path === 'accept' }));

      expect(decision).toEqual({ clubId: 2, accepted: path === 'accept' });
    });
  }

  it('redeems a shared link against its own endpoint', () => {
    let decision: unknown;
    service.redeemLink('tok-link').subscribe((result) => (decision = result));

    const request = httpMock.expectOne(`${linkBase}/redeem`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ token: 'tok-link' });
    request.flush(pascalEnvelope({ ClubId: 2, Accepted: true }));

    expect(decision).toEqual({ clubId: 2, accepted: true });
  });

  it('errors with the envelope message when the response has no data', () => {
    let thrown: Error | undefined;
    service.redeemLink('tok-link').subscribe({ error: (err: Error) => (thrown = err) });

    httpMock
      .expectOne(`${linkBase}/redeem`)
      .flush(envelope(null, { message: 'This link has been revoked.' }));

    expect(thrown?.message).toBe('This link has been revoked.');
  });
});
