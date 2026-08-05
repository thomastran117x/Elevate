import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';

import { ClubInvitationsService } from './club-invitations.service';

describe('ClubInvitationsService', () => {
  const base = `${environment.backendUrl}/clubs/invitations`;
  let service: ClubInvitationsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(ClubInvitationsService));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('resolves a token and normalizes the PascalCase response', () => {
    let resolved: unknown;
    service.resolve('tok-1').subscribe((result) => (resolved = result));

    const request = httpMock.expectOne(`${base}/resolve`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ token: 'tok-1' });
    expect(request.request.withCredentials).toBeTrue();
    request.flush(
      pascalEnvelope({
        State: 'Pending',
        CanAccept: true,
        Role: 'Volunteer',
        Club: { Id: 3, Name: 'Robotics Club', ClubImage: '' },
      }),
    );

    expect(resolved).toEqual(
      jasmine.objectContaining({
        state: 'Pending',
        canAccept: true,
        role: 'Volunteer',
        club: { id: 3, name: 'Robotics Club', clubImage: '' },
      }),
    );
  });

  for (const [method, path] of [
    ['accept', 'accept'],
    ['decline', 'decline'],
  ] as const) {
    it(`posts the token to ${path} and normalizes the decision`, () => {
      let decision: unknown;
      service[method]('tok-1').subscribe((result) => (decision = result));

      const request = httpMock.expectOne(`${base}/${path}`);
      expect(request.request.body).toEqual({ token: 'tok-1' });
      request.flush(pascalEnvelope({ ClubId: 3, Role: 'Manager', Accepted: path === 'accept' }));

      expect(decision).toEqual({ clubId: 3, role: 'Manager', accepted: path === 'accept' });
    });
  }

  it('errors with the envelope message when the response has no data', () => {
    let thrown: Error | undefined;
    service.resolve('tok-1').subscribe({ error: (err: Error) => (thrown = err) });

    httpMock
      .expectOne(`${base}/resolve`)
      .flush(envelope(null, { message: 'That invitation has expired.' }));

    expect(thrown?.message).toBe('That invitation has expired.');
  });

  it('falls back to a generic message when the envelope has none', () => {
    let thrown: Error | undefined;
    service.accept('tok-1').subscribe({ error: (err: Error) => (thrown = err) });

    httpMock.expectOne(`${base}/accept`).flush(envelope(null, { message: '' }));

    expect(thrown?.message).toBe('Invitation response was incomplete.');
  });
});
