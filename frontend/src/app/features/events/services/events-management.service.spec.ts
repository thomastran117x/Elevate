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

  describe('gallery management', () => {
    const wireImage = (overrides: Record<string, unknown> = {}) => ({
      id: 9,
      url: 'https://cdn.test/a.png',
      altText: 'A packed main stage',
      isDecorative: false,
      isCover: true,
      sortOrder: 0,
      needsAltText: false,
      createdAt: '2026-05-01T00:00:00Z',
      updatedAt: '2026-05-02T00:00:00Z',
      ...overrides,
    });

    it('normalizes the gallery from a camelCase payload', async () => {
      const pending = firstValueFrom(service.getEventImages(12));

      const request = httpMock.expectOne(`${base}/12/images`);
      expect(request.request.method).toBe('GET');
      request.flush(envelope([wireImage()]));

      const images = await pending;
      expect(images).toEqual([
        {
          id: 9,
          url: 'https://cdn.test/a.png',
          altText: 'A packed main stage',
          isDecorative: false,
          isCover: true,
          sortOrder: 0,
          needsAltText: false,
          createdAt: '2026-05-01T00:00:00Z',
          updatedAt: '2026-05-02T00:00:00Z',
        },
      ]);
    });

    it('normalizes a PascalCase gallery payload', async () => {
      const pending = firstValueFrom(service.getEventImages(12));

      httpMock.expectOne(`${base}/12/images`).flush(
        pascalEnvelope([
          {
            Id: 4,
            Url: 'https://cdn.test/b.png',
            AltText: null,
            IsDecorative: false,
            IsCover: false,
            SortOrder: 1,
            NeedsAltText: true,
            CreatedAt: '2026-05-01T00:00:00Z',
            UpdatedAt: '2026-05-01T00:00:00Z',
          },
        ]),
      );

      const images = await pending;
      expect(images[0].id).toBe(4);
      expect(images[0].url).toBe('https://cdn.test/b.png');
      expect(images[0].needsAltText).toBeTrue();
    });

    it('derives needsAltText when the payload omits it', async () => {
      const pending = firstValueFrom(service.getEventImages(12));

      httpMock
        .expectOne(`${base}/12/images`)
        .flush(envelope([{ id: 7, url: 'https://cdn.test/c.png' }]));

      const images = await pending;
      expect(images[0].needsAltText).toBeTrue();
    });

    it('sends alt text when attaching a described image', () => {
      service
        .addEventImage(12, { imageUrl: 'https://cdn.test/a.png', altText: 'Main stage' })
        .subscribe();

      const request = httpMock.expectOne(`${base}/12/images`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        imageUrl: 'https://cdn.test/a.png',
        altText: 'Main stage',
        isDecorative: false,
        isCover: false,
      });
      request.flush(envelope(wireImage()));
    });

    it('drops alt text when the image is marked decorative', () => {
      service
        .addEventImage(12, {
          imageUrl: 'https://cdn.test/a.png',
          altText: 'ignored',
          isDecorative: true,
        })
        .subscribe();

      const request = httpMock.expectOne(`${base}/12/images`);
      // The server rejects an image holding both, so one of them has to give.
      expect(request.request.body.altText).toBeNull();
      expect(request.request.body.isDecorative).toBeTrue();
      request.flush(envelope(wireImage({ isDecorative: true, altText: null })));
    });

    it('patches metadata without touching the image', () => {
      service.updateEventImage(12, 9, { altText: 'Updated text' }).subscribe();

      const request = httpMock.expectOne(`${base}/12/images/9`);
      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ altText: 'Updated text', isDecorative: false });
      request.flush(envelope(wireImage({ altText: 'Updated text' })));
    });

    it('puts the full id order when reordering', async () => {
      const pending = firstValueFrom(service.reorderEventImages(12, [3, 1, 2]));

      const request = httpMock.expectOne(`${base}/12/images/order`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ imageIds: [3, 1, 2] });
      request.flush(envelope([wireImage({ id: 3 }), wireImage({ id: 1 }), wireImage({ id: 2 })]));

      expect((await pending).map((image) => image.id)).toEqual([3, 1, 2]);
    });

    it('sets the cover and returns the whole gallery', async () => {
      const pending = firstValueFrom(service.setEventCoverImage(12, 9));

      const request = httpMock.expectOne(`${base}/12/images/9/cover`);
      expect(request.request.method).toBe('PUT');
      request.flush(
        envelope([wireImage({ id: 9, isCover: true }), wireImage({ id: 4, isCover: false })]),
      );

      const gallery = await pending;
      expect(gallery.filter((image) => image.isCover).length).toBe(1);
    });

    it('replaces the file behind an image', () => {
      service.replaceEventImage(12, 9, 'https://cdn.test/new.png').subscribe();

      const request = httpMock.expectOne(`${base}/12/images/9`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ imageUrl: 'https://cdn.test/new.png' });
      request.flush(envelope(wireImage({ url: 'https://cdn.test/new.png' })));
    });

    it('reads a single image back from a PascalCase envelope', async () => {
      const pending = firstValueFrom(
        service.addEventImage(12, { imageUrl: 'https://cdn.test/a.png' }),
      );

      httpMock.expectOne(`${base}/12/images`).flush(
        pascalEnvelope({
          Id: 11,
          Url: 'https://cdn.test/a.png',
          AltText: 'Described',
          IsDecorative: false,
          IsCover: false,
          SortOrder: 2,
          NeedsAltText: false,
          CreatedAt: '2026-05-01T00:00:00Z',
          UpdatedAt: '2026-05-01T00:00:00Z',
        }),
      );

      const image = await pending;
      expect(image.id).toBe(11);
      expect(image.altText).toBe('Described');
    });

    it('survives an empty body rather than throwing on the caller', async () => {
      const pending = firstValueFrom(service.updateEventImage(12, 9, { isDecorative: true }));

      httpMock.expectOne(`${base}/12/images/9`).flush(envelope(null));

      const image = await pending;
      expect(image.id).toBe(0);
      expect(image.url).toBe('');
    });

    it('treats a null gallery as empty', async () => {
      const pending = firstValueFrom(service.reorderEventImages(12, [1]));

      httpMock.expectOne(`${base}/12/images/order`).flush(envelope(null));

      expect(await pending).toEqual([]);
    });

    it('defaults the flags when attaching with only a URL', () => {
      service.addEventImage(12, { imageUrl: 'https://cdn.test/a.png' }).subscribe();

      const request = httpMock.expectOne(`${base}/12/images`);
      expect(request.request.body).toEqual({
        imageUrl: 'https://cdn.test/a.png',
        altText: null,
        isDecorative: false,
        isCover: false,
      });
      request.flush(envelope(wireImage()));
    });

    it('can attach an image as the cover straight away', () => {
      service.addEventImage(12, { imageUrl: 'https://cdn.test/a.png', isCover: true }).subscribe();

      const request = httpMock.expectOne(`${base}/12/images`);
      expect(request.request.body.isCover).toBeTrue();
      request.flush(envelope(wireImage()));
    });

    it('removes an image', () => {
      service.removeEventImage(12, 9).subscribe();

      const request = httpMock.expectOne(`${base}/12/images/9`);
      expect(request.request.method).toBe('DELETE');
      request.flush({});
    });
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
