import { HttpTestingController } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '@environments/environment';
import { envelope, pascalEnvelope, setupService } from '@testing';

import { EventsManagementService } from './events-management.service';
import { ManagedEvent } from '../models/event.types';

describe('EventsManagementService', () => {
  const base = `${environment.backendUrl}/events`;
  let service: EventsManagementService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(EventsManagementService));
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('getManageableEvents', () => {
    const url = `${base}/clubs/3/manage`;

    it('serializes only the filters that are set', () => {
      service
        .getManageableEvents(3, {
          lifecycleState: 'Draft',
          page: 2,
          pageSize: 5,
          search: '  robotics  ',
        })
        .subscribe();

      const request = httpMock.expectOne((req) => req.url === url);
      expect(request.request.method).toBe('GET');
      expect(request.request.params.get('lifecycleState')).toBe('Draft');
      expect(request.request.params.get('page')).toBe('2');
      expect(request.request.params.get('pageSize')).toBe('5');
      expect(request.request.params.get('search')).toBe('robotics');
      request.flush(envelope(null));
    });

    it('omits blank and zero filters', () => {
      service.getManageableEvents(3, { search: '   ', page: 0, pageSize: 0 }).subscribe();

      const request = httpMock.expectOne((req) => req.url === url);
      expect(request.request.params.keys()).toEqual([]);
      request.flush(envelope(null));
    });

    it('normalizes a PascalCase paged payload and applies the paging defaults', () => {
      let data: unknown;
      service.getManageableEvents(3, {}).subscribe((response) => (data = response.data));

      httpMock
        .expectOne((req) => req.url === url)
        .flush(pascalEnvelope({ Items: [{ Id: 1, Name: 'Kickoff' }] }));

      expect(data).toEqual(
        jasmine.objectContaining({
          page: 1,
          pageSize: 20,
          totalCount: 0,
          items: [jasmine.objectContaining({ id: 1, name: 'Kickoff' })],
        }),
      );
    });

    it('leaves data null when the envelope carries none', () => {
      let data: unknown = 'untouched';
      service.getManageableEvents(3, {}).subscribe((response) => (data = response.data));

      httpMock.expectOne((req) => req.url === url).flush(envelope(null));

      expect(data).toBeNull();
    });
  });

  describe('enum normalization', () => {
    function readEvent(payload: Record<string, unknown>): ManagedEvent {
      let event: ManagedEvent | null = null;
      service.getManageableEvent(1).subscribe((response) => (event = response.data));
      httpMock.expectOne(`${base}/1/manage`).flush(pascalEnvelope(payload));
      return event as unknown as ManagedEvent;
    }

    it('maps numeric enums to their labels', () => {
      const event = readEvent({ Id: 1, LifecycleState: 1, Status: 0, Category: 0 });

      expect(event.lifecycleState).toBe('Published');
      expect(event.status).toBe('Upcoming');
      expect(event.category).toBe('Sports');
    });

    it('passes known string enums through', () => {
      const event = readEvent({ Id: 1, LifecycleState: 'Cancelled', Category: 'Music' });

      expect(event.lifecycleState).toBe('Cancelled');
      expect(event.category).toBe('Music');
    });

    it('falls back for unknown enum values', () => {
      const event = readEvent({ Id: 1, LifecycleState: 'Zombie', Category: 'Interpretive Dance' });

      expect(event.lifecycleState).toBe('Draft');
      expect(event.category).toBe('Other');
    });

    it('decodes the Paused ordinal, which only works while it stays last', () => {
      // The API has no string-enum converter, so lifecycle states arrive as integers and are
      // decoded by index. Paused must sit at 4 or every stored event shifts meaning.
      expect(readEvent({ Id: 1, LifecycleState: 4 }).lifecycleState).toBe('Paused');
      expect(readEvent({ Id: 1, LifecycleState: 'Paused' }).lifecycleState).toBe('Paused');
    });

    it('normalizes the lifecycle transitions the server advertises', () => {
      const event = readEvent({
        Id: 1,
        LifecycleState: 1,
        AvailableTransitions: [
          {
            Key: 'pause',
            Target: 4,
            Label: 'Pause event',
            Title: 'Pause this event?',
            IsReversible: true,
            ReversibleNote: 'Reversible — resume any time.',
            IsDestructive: false,
            Impacts: ['It is removed from public search and listings.'],
            BlockedReason: null,
          },
        ],
      });

      expect(event.availableTransitions).toEqual([
        {
          key: 'pause',
          target: 'Paused',
          label: 'Pause event',
          title: 'Pause this event?',
          isReversible: true,
          reversibleNote: 'Reversible — resume any time.',
          isDestructive: false,
          impacts: ['It is removed from public search and listings.'],
          blockedReason: null,
        },
      ]);
    });

    it('keeps a missing previous state null rather than defaulting it to Draft', () => {
      // An event that has never changed state has no previous state, which is a different
      // thing from being a draft.
      const event = readEvent({ Id: 1, LifecycleState: 1 });

      expect(event.previousLifecycleState).toBeNull();
      expect(event.revertAvailableUntil).toBeNull();
      expect(event.availableTransitions).toEqual([]);

      expect(readEvent({ Id: 1, PreviousLifecycleState: 1 }).previousLifecycleState).toBe(
        'Published',
      );
    });

    it('leaves status undefined when the payload omits it', () => {
      expect(readEvent({ Id: 1 }).status).toBeUndefined();
    });

    it('defaults collections and series fields rather than emitting undefined', () => {
      const event = readEvent({ Id: 1 });

      expect(event.imageUrls).toEqual([]);
      expect(event.tags).toEqual([]);
      expect(event.publishIssues).toEqual([]);
      expect(event.seriesId).toBeNull();
      expect(event.occurrenceIndex).toBeNull();
      expect(event.timeZoneId).toBeNull();
      expect(event.seriesOverridden).toBeFalse();
    });
  });

  describe('draft mutations', () => {
    it('posts a new draft to the club drafts endpoint', () => {
      service.createDraft(3, { name: 'Kickoff' }).subscribe();

      const request = httpMock.expectOne(`${base}/clubs/3/drafts`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'Kickoff' });
      request.flush(pascalEnvelope({ Id: 1 }));
    });

    it('patches an existing draft', () => {
      service.updateDraft(7, { name: 'Renamed' }).subscribe();

      const request = httpMock.expectOne(`${base}/7/draft`);
      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ name: 'Renamed' });
      request.flush(pascalEnvelope({ Id: 7 }));
    });
  });

  describe('lifecycle transitions', () => {
    for (const [method, path] of [
      ['publishEvent', 'publish'],
      ['cancelEvent', 'cancel'],
      ['archiveEvent', 'archive'],
      ['pauseEvent', 'pause'],
      ['resumeEvent', 'resume'],
      ['reinstateEvent', 'reinstate'],
      ['unarchiveEvent', 'unarchive'],
      ['revertLifecycle', 'lifecycle/revert'],
    ] as const) {
      it(`posts an empty body to ${path}`, () => {
        service[method](7).subscribe();

        const request = httpMock.expectOne(`${base}/7/${path}`);
        expect(request.request.method).toBe('POST');
        expect(request.request.body).toEqual({});
        request.flush(pascalEnvelope({ Id: 7 }));
      });
    }
  });

  describe('runTransition', () => {
    it('posts to the key the server supplied, so no switch has to track the states', () => {
      service.runTransition(7, 'reinstate').subscribe();

      const request = httpMock.expectOne(`${base}/7/reinstate`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(pascalEnvelope({ Id: 7 }));
    });
  });

  describe('deleteEvent', () => {
    it('issues a DELETE for the event', () => {
      service.deleteEvent(7).subscribe();

      const request = httpMock.expectOne(`${base}/7`);
      expect(request.request.method).toBe('DELETE');
      request.flush({});
    });
  });

  describe('uploadImage', () => {
    const file = new File(['bytes'], 'poster.png', { type: 'image/png' });

    it('requests a presigned URL, PUTs the file, then yields the public URL', async () => {
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo({ ok: true } as Response);

      const result = firstValueFrom(service.uploadImage(3, file, 7));

      const request = httpMock.expectOne(`${base}/images/presigned-url`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        clubId: 3,
        eventId: 7,
        fileName: 'poster.png',
        contentType: 'image/png',
      });
      request.flush(
        envelope({
          uploadUrl: 'https://blob.example.com/put',
          publicUrl: 'https://cdn/poster.png',
        }),
      );

      await expectAsync(result).toBeResolvedTo('https://cdn/poster.png');

      const [url, init] = fetchSpy.calls.mostRecent().args as [string, RequestInit];
      expect(url).toBe('https://blob.example.com/put');
      expect(init.method).toBe('PUT');
      // Azure Blob rejects the SAS PUT without this header.
      expect((init.headers as Record<string, string>)['x-ms-blob-type']).toBe('BlockBlob');
      expect(init.body).toBe(file);
    });

    it('accepts a PascalCase presigned payload', async () => {
      spyOn(window, 'fetch').and.resolveTo({ ok: true } as Response);

      const result = firstValueFrom(service.uploadImage(3, file));

      httpMock
        .expectOne(`${base}/images/presigned-url`)
        .flush(pascalEnvelope({ UploadUrl: 'https://blob/put', PublicUrl: 'https://cdn/p.png' }));

      await expectAsync(result).toBeResolvedTo('https://cdn/p.png');
    });

    it('fails when the presigned response is incomplete', async () => {
      const result = firstValueFrom(service.uploadImage(3, file));

      httpMock
        .expectOne(`${base}/images/presigned-url`)
        .flush(envelope({ uploadUrl: 'https://blob/put' }));

      await expectAsync(result).toBeRejectedWithError('The upload URL could not be prepared.');
    });

    it('fails when the blob PUT is rejected', async () => {
      spyOn(window, 'fetch').and.resolveTo({ ok: false } as Response);

      const result = firstValueFrom(service.uploadImage(3, file));

      httpMock
        .expectOne(`${base}/images/presigned-url`)
        .flush(envelope({ uploadUrl: 'https://blob/put', publicUrl: 'https://cdn/p.png' }));

      await expectAsync(result).toBeRejectedWithError('The image upload failed.');
    });

    it('defaults the content type for a file the browser could not type', async () => {
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo({ ok: true } as Response);
      const untyped = new File(['bytes'], 'blob.bin', { type: '' });

      const result = firstValueFrom(service.uploadImage(3, untyped));

      const request = httpMock.expectOne(`${base}/images/presigned-url`);
      expect(request.request.body.contentType).toBe('application/octet-stream');
      request.flush(envelope({ uploadUrl: 'https://blob/put', publicUrl: 'https://cdn/b.bin' }));

      await result;
      const [, init] = fetchSpy.calls.mostRecent().args as [string, RequestInit];
      expect((init.headers as Record<string, string>)['Content-Type']).toBe(
        'application/octet-stream',
      );
    });
  });
});
