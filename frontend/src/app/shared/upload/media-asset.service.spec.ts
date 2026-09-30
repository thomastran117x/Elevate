import { PLATFORM_ID } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '@environments/environment';
import { envelope, errorEnvelope, pascalEnvelope, setupService } from '@testing';

import {
  MEDIA_POLL_CEILING_MS,
  MediaAssetService,
  MediaAssetWatchEvent,
  isSettled,
  nextPollDelay,
  normalizeMediaAsset,
} from './media-asset.service';

describe('MediaAssetService', () => {
  const assetId = '0199a1b2-0000-7000-8000-000000000001';
  const url = `${environment.backendUrl}/media/${assetId}`;
  let service: MediaAssetService;
  let httpMock: HttpTestingController;

  const statusBody = (status: number, extra: Record<string, unknown> = {}) =>
    envelope({ id: assetId, status, url: null, rejectionReason: null, ...extra });

  afterEach(() => httpMock.verify());

  describe('in the browser', () => {
    beforeEach(() => {
      ({ service, httpMock } = setupService(MediaAssetService));
    });

    it('reads an asset, decoding the numeric status by position', async () => {
      const result = firstValueFrom(service.getStatus(assetId));

      httpMock.expectOne(url).flush(statusBody(3, { url: 'https://cdn/events/a.webp' }));

      await expectAsync(result).toBeResolvedTo({
        id: assetId,
        status: 'ready',
        url: 'https://cdn/events/a.webp',
        rejectionReason: null,
      });
    });

    it('accepts a PascalCase payload', async () => {
      const result = firstValueFrom(service.getStatus(assetId));

      httpMock
        .expectOne(url)
        .flush(pascalEnvelope({ Id: assetId, Status: 4, Url: null, RejectionReason: 'Too big.' }));

      await expectAsync(result).toBeResolvedTo({
        id: assetId,
        status: 'rejected',
        url: null,
        rejectionReason: 'Too big.',
      });
    });

    it('fails on a payload it cannot read', async () => {
      const result = firstValueFrom(service.getStatus(assetId));

      httpMock.expectOne(url).flush(envelope({ id: assetId, status: 42 }));

      await expectAsync(result).toBeRejectedWithError('The image status could not be read.');
    });

    it('reads immediately, then stops once the asset is ready', fakeAsync(() => {
      const events: MediaAssetWatchEvent[] = [];
      let completed = false;
      service
        .watch(assetId)
        .subscribe({ next: (e) => events.push(e), complete: () => (completed = true) });

      httpMock.expectOne(url).flush(statusBody(2));
      tick(1499);
      httpMock.expectNone(url);
      tick(1);
      httpMock.expectOne(url).flush(statusBody(3, { url: 'https://cdn/a.webp' }));

      expect(events.map((e) => (e.kind === 'status' ? e.asset.status : e.kind))).toEqual([
        'processing',
        'ready',
      ]);
      expect(completed).toBeTrue();

      tick(MEDIA_POLL_CEILING_MS);
      httpMock.expectNone(url);
    }));

    it('stops once the asset is rejected', fakeAsync(() => {
      const events: MediaAssetWatchEvent[] = [];
      service.watch(assetId).subscribe((e) => events.push(e));

      httpMock
        .expectOne(url)
        .flush(statusBody(4, { rejectionReason: 'Animated images are not supported.' }));

      expect(events).toEqual([
        {
          kind: 'status',
          asset: {
            id: assetId,
            status: 'rejected',
            url: null,
            rejectionReason: 'Animated images are not supported.',
          },
        },
      ]);
      tick(MEDIA_POLL_CEILING_MS);
      httpMock.expectNone(url);
    }));

    it('slows down after ten seconds, then gives up at the ceiling with a slow event', fakeAsync(() => {
      const events: MediaAssetWatchEvent[] = [];
      let completed = false;
      service
        .watch(assetId)
        .subscribe({ next: (e) => events.push(e), complete: () => (completed = true) });

      const readTimes: number[] = [];
      let elapsed = 0;
      while (!completed && elapsed <= MEDIA_POLL_CEILING_MS + 5000) {
        for (const request of httpMock.match(url)) {
          readTimes.push(elapsed);
          request.flush(statusBody(2));
        }
        tick(500);
        elapsed += 500;
      }

      // Every 1.5 s until ten seconds have passed, then every 4 s, never past a minute.
      expect(readTimes.slice(0, 8)).toEqual([0, 1500, 3000, 4500, 6000, 7500, 9000, 10500]);
      expect(readTimes[8]).toBe(14500);
      expect(readTimes[readTimes.length - 1]).toBeLessThanOrEqual(MEDIA_POLL_CEILING_MS);
      expect(events[events.length - 1]).toEqual({ kind: 'slow' });
      expect(completed).toBeTrue();
    }));

    it('keeps polling through a failed read', fakeAsync(() => {
      const events: MediaAssetWatchEvent[] = [];
      service.watch(assetId).subscribe((e) => events.push(e));

      httpMock.expectOne(url).flush(errorEnvelope('INTERNAL_SERVER_ERROR', 'boom'), {
        status: 500,
        statusText: 'Server Error',
      });
      tick(1500);
      httpMock.expectOne(url).flush(statusBody(3));

      expect(events.length).toBe(1);
      expect(events[0]).toEqual(jasmine.objectContaining({ kind: 'status' }));
    }));

    it('ends quietly when the asset is not found', fakeAsync(() => {
      const events: MediaAssetWatchEvent[] = [];
      let completed = false;
      service
        .watch(assetId)
        .subscribe({ next: (e) => events.push(e), complete: () => (completed = true) });

      httpMock.expectOne(url).flush(errorEnvelope('RESOURCE_NOT_FOUND', 'Media asset not found.'), {
        status: 404,
        statusText: 'Not Found',
      });

      expect(events).toEqual([]);
      expect(completed).toBeTrue();
      tick(MEDIA_POLL_CEILING_MS);
      httpMock.expectNone(url);
    }));
  });

  describe('during server-side rendering', () => {
    beforeEach(() => {
      ({ service, httpMock } = setupService(MediaAssetService, [
        { provide: PLATFORM_ID, useValue: 'server' },
      ]));
    });

    it('watches nothing', () => {
      let completed = false;
      service.watch(assetId).subscribe({ complete: () => (completed = true) });

      expect(completed).toBeTrue();
      httpMock.expectNone(url);
    });
  });
});

// Pure helpers: no TestBed, so they sit outside the service's HTTP verification.
describe('MediaAssetService helpers', () => {
  it('treats only ready and rejected as settled', () => {
    expect(isSettled('ready')).toBeTrue();
    expect(isSettled('rejected')).toBeTrue();
    expect(isSettled('processing')).toBeFalse();
    expect(isSettled('needsReview')).toBeFalse();
  });

  it('schedules fast reads for ten seconds, slow ones after, and none past a minute', () => {
    expect(nextPollDelay(0)).toBe(1500);
    expect(nextPollDelay(9000)).toBe(1500);
    expect(nextPollDelay(10_500)).toBe(4000);
    expect(nextPollDelay(56_000)).toBe(4000);
    expect(nextPollDelay(58_500)).toBeNull();
  });

  it('rejects payloads without an id or a known status', () => {
    expect(normalizeMediaAsset(null)).toBeNull();
    expect(normalizeMediaAsset({ status: 3 })).toBeNull();
    expect(normalizeMediaAsset({ id: 'a', status: -1 })).toBeNull();
    expect(normalizeMediaAsset({ id: 'a', status: 1.5 })).toBeNull();
    expect(normalizeMediaAsset({ id: 'a', status: 5 })).toEqual({
      id: 'a',
      status: 'needsReview',
      url: null,
      rejectionReason: null,
    });
  });
});
