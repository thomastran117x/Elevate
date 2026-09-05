import { HttpTestingController } from '@angular/common/http/testing';

import { envelope, makeEventItem, pascalEnvelope, setupService } from '@testing';

import { PinnedEvent } from '../models/event-favourite.types';
import { EventFavouritesService } from './event-favourites.service';

describe('EventFavouritesService', () => {
  let service: EventFavouritesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ service, httpMock } = setupService(EventFavouritesService));
  });

  afterEach(() => httpMock.verify());

  it('posts to the favourite endpoint with credentials', () => {
    let favourited = false;
    service.favourite(42).subscribe((result) => (favourited = result.isFavourited));

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/favourite'));
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(
      envelope({ eventId: 42, isFavourited: true, favouritedAtUtc: '2026-08-07T00:00:00Z' }),
    );

    expect(favourited).toBeTrue();
  });

  it('deletes the favourite with credentials', () => {
    let completed = false;
    service.unfavourite(42).subscribe(() => (completed = true));

    const request = httpMock.expectOne((r) => r.url.includes('/events/42/favourite'));
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope(null));

    expect(completed).toBeTrue();
  });

  it('reads the favourite status for a single event', () => {
    let favourited = true;
    service.getMyStatus(7).subscribe((result) => (favourited = result.isFavourited));

    const request = httpMock.expectOne((r) => r.url.includes('/events/7/favourite/me'));
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ eventId: 7, isFavourited: false, favouritedAtUtc: null }));

    expect(favourited).toBeFalse();
  });

  it('falls back to the requested id when the payload omits it', () => {
    let eventId = 0;
    service.getMyStatus(7).subscribe((result) => (eventId = result.eventId));

    httpMock.expectOne((r) => r.url.includes('/events/7/favourite/me')).flush(envelope({}));

    expect(eventId).toBe(7);
  });

  it('tolerates the legacy PascalCase envelope', () => {
    let favourited = false;
    service.favourite(42).subscribe((result) => (favourited = result.isFavourited));

    httpMock
      .expectOne((r) => r.url.includes('/events/42/favourite'))
      .flush(pascalEnvelope({ EventId: 42, IsFavourited: true, FavouritedAtUtc: null }));

    expect(favourited).toBeTrue();
  });

  it('fetches the favourited id set', () => {
    let ids: number[] = [];
    service.getMyFavouriteIds().subscribe((result) => (ids = result));

    const request = httpMock.expectOne((r) => r.url.includes('/events/me/favourites/ids'));
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(envelope([12, 47, 88]));

    expect(ids).toEqual([12, 47, 88]);
  });

  it('returns an empty id set when the payload is null', () => {
    let ids: number[] = [1];
    service.getMyFavouriteIds().subscribe((result) => (ids = result));

    httpMock.expectOne((r) => r.url.includes('/events/me/favourites/ids')).flush(envelope(null));

    expect(ids).toEqual([]);
  });

  it('decodes the nested event, whose enums arrive as integers', () => {
    // Same defect as /my-waitlists, reached through a different service: the cast hid that
    // lifecycleState was still a number, so the "Registration paused" row never rendered.
    let pinned: PinnedEvent[] = [];
    service.getMyPinned().subscribe((result) => (pinned = result));

    httpMock
      .expectOne((r) => r.url.includes('/events/me/pinned'))
      .flush(
        envelope([
          { isRegistered: true, event: { id: 9, lifecycleState: 4 } },
          { isRegistered: false, event: { id: 4, lifecycleState: 2 } },
        ]),
      );

    expect(pinned[0].event.lifecycleState).toBe('Paused');
    expect(pinned[1].event.lifecycleState).toBe('Cancelled');
  });

  it('normalizes pinned rows and keeps the server ordering', () => {
    let pinned: PinnedEvent[] = [];
    service.getMyPinned().subscribe((result) => (pinned = result));

    const request = httpMock.expectOne((r) => r.url.includes('/events/me/pinned'));
    expect(request.request.method).toBe('GET');
    request.flush(
      envelope([
        {
          isRegistered: true,
          isFavourited: false,
          registeredAtUtc: '2026-08-01T00:00:00Z',
          favouritedAtUtc: null,
          accessRevoked: false,
          event: makeEventItem({ id: 9, name: 'Going Event' }),
        },
        {
          IsRegistered: false,
          IsFavourited: true,
          FavouritedAtUtc: '2026-08-02T00:00:00Z',
          AccessRevoked: true,
          Event: makeEventItem({ id: 4, name: '' }),
        },
      ]),
    );

    expect(pinned.length).toBe(2);
    expect(pinned[0].event.id).toBe(9);
    expect(pinned[0].isRegistered).toBeTrue();
    expect(pinned[0].isFavourited).toBeFalse();

    // The second row arrives PascalCase and is redacted, which must survive normalization.
    expect(pinned[1].event.id).toBe(4);
    expect(pinned[1].isFavourited).toBeTrue();
    expect(pinned[1].accessRevoked).toBeTrue();
  });

  it('returns an empty pinned list when the payload is null', () => {
    const pinned: PinnedEvent[][] = [];
    service.getMyPinned().subscribe((result) => pinned.push(result));

    httpMock.expectOne((r) => r.url.includes('/events/me/pinned')).flush(envelope(null));

    expect(pinned).toEqual([[]]);
  });
});
