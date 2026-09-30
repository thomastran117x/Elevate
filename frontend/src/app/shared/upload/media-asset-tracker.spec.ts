import { Subject, of } from 'rxjs';

import {
  MEDIA_CHECKING_LABEL,
  MEDIA_REJECTED_FALLBACK,
  MEDIA_SLOW_LABEL,
  MediaAssetTracker,
} from './media-asset-tracker';
import { MediaAsset, MediaAssetService, MediaAssetWatchEvent } from './media-asset.service';

describe('MediaAssetTracker', () => {
  const url = 'https://cdn/events/a.webp';
  let media: jasmine.SpyObj<Pick<MediaAssetService, 'watch'>>;
  let events: Subject<MediaAssetWatchEvent>;
  let tracker: MediaAssetTracker;

  const status = (
    value: MediaAsset['status'],
    rejectionReason: string | null = null,
  ): MediaAssetWatchEvent => ({
    kind: 'status',
    asset: { id: 'asset-1', status: value, url: value === 'ready' ? url : null, rejectionReason },
  });

  beforeEach(() => {
    events = new Subject<MediaAssetWatchEvent>();
    media = jasmine.createSpyObj<Pick<MediaAssetService, 'watch'>>('MediaAssetService', ['watch']);
    media.watch.and.returnValue(events);
    tracker = new MediaAssetTracker(media as unknown as MediaAssetService);
  });

  it('does nothing for an upload without an asset, which is live as soon as it is attached', () => {
    tracker.watch(url, null);

    expect(media.watch).not.toHaveBeenCalled();
    expect(tracker.labelFor(url)).toBeNull();
  });

  it('labels the tile while the image is checked, and calls ready once it is published', () => {
    const ready = jasmine.createSpy('ready');
    tracker.watch(url, 'asset-1', { ready });

    expect(media.watch).toHaveBeenCalledOnceWith('asset-1');
    expect(tracker.stateOf(url)).toBe('checking');
    expect(tracker.labelFor(url)).toBe(MEDIA_CHECKING_LABEL);

    events.next(status('processing'));
    expect(tracker.labelFor(url)).toBe(MEDIA_CHECKING_LABEL);

    events.next(status('ready'));
    expect(ready).toHaveBeenCalledOnceWith(url);
    expect(tracker.labelFor(url)).toBeNull();
    expect(events.observed).toBeFalse();
  });

  it('passes on the reason when the image is rejected', () => {
    const rejected = jasmine.createSpy('rejected');
    tracker.watch(url, 'asset-1', { rejected });

    events.next(status('rejected', 'Animated images are not supported.'));

    expect(rejected).toHaveBeenCalledOnceWith(url, 'Animated images are not supported.');
    expect(tracker.labelFor(url)).toBeNull();
  });

  it('falls back to a generic reason when the server gave none', () => {
    const rejected = jasmine.createSpy('rejected');
    tracker.watch(url, 'asset-1', { rejected });

    events.next(status('rejected'));

    expect(rejected).toHaveBeenCalledOnceWith(url, MEDIA_REJECTED_FALLBACK);
  });

  it('says the image is still processing once polling gives up, and keeps saying so', () => {
    tracker.watch(url, 'asset-1');

    events.next({ kind: 'slow' });
    events.complete();

    expect(tracker.stateOf(url)).toBe('slow');
    expect(tracker.labelFor(url)).toBe(MEDIA_SLOW_LABEL);
  });

  it('stops labelling a tile whose watch ends without an answer', () => {
    tracker.watch(url, 'asset-1');

    events.complete();

    expect(tracker.labelFor(url)).toBeNull();
  });

  it('handles a watch that settles before subscribe returns', () => {
    media.watch.and.returnValue(of(status('ready')));
    const ready = jasmine.createSpy('ready');

    tracker.watch(url, 'asset-1', { ready });

    expect(ready).toHaveBeenCalledOnceWith(url);
    expect(tracker.labelFor(url)).toBeNull();
  });

  it('replaces an earlier watch of the same image', () => {
    const first = new Subject<MediaAssetWatchEvent>();
    const second = new Subject<MediaAssetWatchEvent>();
    media.watch.and.returnValues(first, second);

    tracker.watch(url, 'asset-1');
    tracker.watch(url, 'asset-2');

    expect(first.observed).toBeFalse();
    expect(second.observed).toBeTrue();
  });

  it('stops one poll on stop, and every poll on stopAll', () => {
    const other = 'https://cdn/events/b.webp';
    const otherEvents = new Subject<MediaAssetWatchEvent>();
    media.watch.and.returnValues(events, otherEvents);
    tracker.watch(url, 'asset-1');
    tracker.watch(other, 'asset-2');

    tracker.stop(url);
    expect(events.observed).toBeFalse();
    expect(tracker.labelFor(url)).toBeNull();
    expect(tracker.labelFor(other)).toBe(MEDIA_CHECKING_LABEL);

    tracker.stopAll();
    expect(otherEvents.observed).toBeFalse();
    expect(tracker.labelFor(other)).toBeNull();
  });
});
