import { HttpTestingController } from '@angular/common/http/testing';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';

import { EventInvitationsService } from './event-invitations.service';
import { EventInvitation, EventInvitationLink } from '../models/event-invitation.types';

describe('EventInvitationsService', () => {
  const base = `${environment.backendUrl}/events`;
  let service: EventInvitationsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(EventInvitationsService));
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('token flow', () => {
    it('resolves a token and normalizes the nested summary event', () => {
      let resolved: unknown;
      service.resolve('tok-1').subscribe((result) => (resolved = result));

      const request = httpMock.expectOne(`${base}/invitations/resolve`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ token: 'tok-1' });
      expect(request.request.withCredentials).toBeTrue();
      request.flush(
        pascalEnvelope({
          State: 'Pending',
          CanAccept: true,
          SourceType: 'DirectInvite',
          Event: { Id: 7, Name: 'Kickoff', StartTime: '2026-09-01T18:00:00Z' },
        }),
      );

      expect(resolved).toEqual(
        jasmine.objectContaining({
          state: 'Pending',
          canAccept: true,
          canDecline: false,
          sourceType: 'DirectInvite',
          event: jasmine.objectContaining({ id: 7, name: 'Kickoff', imageUrls: [] }),
        }),
      );
    });

    it('leaves the event undefined when the resolve payload omits it', () => {
      let resolved: { event?: unknown } | undefined;
      service.resolve('tok-1').subscribe((result) => (resolved = result));

      httpMock.expectOne(`${base}/invitations/resolve`).flush(pascalEnvelope({ State: 'Expired' }));

      expect(resolved?.event).toBeUndefined();
    });

    for (const path of ['accept', 'decline'] as const) {
      it(`posts the token to ${path} and unwraps the invitation`, () => {
        let decision: { invitation: EventInvitation } | undefined;
        service[path]('tok-1').subscribe((result) => (decision = result));

        const request = httpMock.expectOne(`${base}/invitations/${path}`);
        expect(request.request.body).toEqual({ token: 'tok-1' });
        request.flush(
          pascalEnvelope({ Invitation: { Id: 5, EventId: 7, LifecycleStatus: 'Accepted' } }),
        );

        expect(decision?.invitation).toEqual(
          jasmine.objectContaining({ id: 5, eventId: 7, lifecycleStatus: 'Accepted' }),
        );
      });
    }

    it('errors when a decision response carries no invitation', () => {
      let thrown: Error | undefined;
      service.accept('tok-1').subscribe({ error: (err: Error) => (thrown = err) });

      httpMock.expectOne(`${base}/invitations/accept`).flush(pascalEnvelope({}));

      expect(thrown?.message).toBe('Invitation response was incomplete.');
    });

    it('errors with the envelope message when the response has no data', () => {
      let thrown: Error | undefined;
      service.resolve('tok-1').subscribe({ error: (err: Error) => (thrown = err) });

      httpMock
        .expectOne(`${base}/invitations/resolve`)
        .flush(envelope(null, { message: 'That invitation has expired.' }));

      expect(thrown?.message).toBe('That invitation has expired.');
    });
  });

  describe('by-id flow', () => {
    for (const path of ['accept', 'decline'] as const) {
      it(`posts an empty body to ${path} for an invitation id`, () => {
        const method = path === 'accept' ? 'acceptById' : 'declineById';
        service[method](5).subscribe();

        const request = httpMock.expectOne(`${base}/invitations/5/${path}`);
        expect(request.request.method).toBe('POST');
        expect(request.request.body).toEqual({});
        request.flush(pascalEnvelope({ Invitation: { Id: 5 } }));
      });
    }
  });

  describe('invitation lists', () => {
    it('normalizes the signed-in user’s invitations', () => {
      let invitations: EventInvitation[] = [];
      service.getMine().subscribe((result) => (invitations = result));

      const request = httpMock.expectOne(`${base}/me/invited`);
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBeTrue();
      request.flush(
        pascalEnvelope([
          { Id: 1, EventId: 7, EffectiveStatus: 'Pending', Event: { Id: 7, Name: 'Kickoff' } },
        ]),
      );

      expect(invitations.length).toBe(1);
      expect(invitations[0]).toEqual(
        jasmine.objectContaining({ id: 1, eventId: 7, effectiveStatus: 'Pending' }),
      );
      expect(invitations[0].event?.name).toBe('Kickoff');
    });

    it('lists the invitations for one event', () => {
      let invitations: EventInvitation[] = [];
      service.getEventInvitations(7).subscribe((result) => (invitations = result));

      httpMock.expectOne(`${base}/7/invitations`).flush(pascalEnvelope([{ Id: 1 }, { Id: 2 }]));

      expect(invitations.map((i) => i.id)).toEqual([1, 2]);
    });

    it('sends the create payload and normalizes what comes back', () => {
      let invitations: EventInvitation[] = [];
      service
        .createInvitations(7, { emails: ['a@example.com'] })
        .subscribe((result) => (invitations = result));

      const request = httpMock.expectOne(`${base}/7/invitations`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ emails: ['a@example.com'] });
      request.flush(pascalEnvelope([{ Id: 3, RecipientEmail: 'a@example.com' }]));

      expect(invitations[0].recipientEmail).toBe('a@example.com');
    });

    it('revokes a single invitation', () => {
      let invitation: EventInvitation | undefined;
      service.revokeInvitation(7, 3).subscribe((result) => (invitation = result));

      const request = httpMock.expectOne(`${base}/7/invitations/3/revoke`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(pascalEnvelope({ Id: 3, LifecycleStatus: 'Revoked' }));

      expect(invitation?.lifecycleStatus).toBe('Revoked');
    });

    it('defaults every string field when the payload is bare', () => {
      let invitations: EventInvitation[] = [];
      service.getMine().subscribe((result) => (invitations = result));

      httpMock.expectOne(`${base}/me/invited`).flush(pascalEnvelope([{}]));

      expect(invitations[0]).toEqual(
        jasmine.objectContaining({
          id: 0,
          eventId: 0,
          sourceType: '',
          lifecycleStatus: '',
          effectiveStatus: '',
          deliveryStatus: '',
          createdAt: '',
          updatedAt: '',
        }),
      );
    });
  });

  describe('invitation links', () => {
    it('lists and normalizes links', () => {
      let links: EventInvitationLink[] = [];
      service.getInvitationLinks(7).subscribe((result) => (links = result));

      const request = httpMock.expectOne(`${base}/7/invitation-links`);
      expect(request.request.method).toBe('GET');
      request.flush(
        pascalEnvelope([
          { Id: 1, EventId: 7, ShareUrl: 'https://e/x', MaxRedemptions: 10, RedemptionCount: 2 },
        ]),
      );

      expect(links[0]).toEqual(
        jasmine.objectContaining({
          id: 1,
          eventId: 7,
          shareUrl: 'https://e/x',
          maxRedemptions: 10,
          redemptionCount: 2,
          isRevoked: false,
        }),
      );
    });

    it('creates a link from the supplied payload', () => {
      const payload = { maxRedemptions: 25, expiresAt: '2026-09-01T00:00:00Z' };
      service.createInvitationLink(7, payload).subscribe();

      const request = httpMock.expectOne(`${base}/7/invitation-links`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(payload);
      request.flush(pascalEnvelope({ Id: 1 }));
    });

    it('revokes a link', () => {
      let link: EventInvitationLink | undefined;
      service.revokeInvitationLink(7, 1).subscribe((result) => (link = result));

      const request = httpMock.expectOne(`${base}/7/invitation-links/1/revoke`);
      expect(request.request.body).toEqual({});
      request.flush(pascalEnvelope({ Id: 1, IsRevoked: true }));

      expect(link?.isRevoked).toBeTrue();
    });
  });
});
