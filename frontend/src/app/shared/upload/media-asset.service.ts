import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import {
  EMPTY,
  Observable,
  catchError,
  concat,
  expand,
  filter,
  map,
  of,
  switchMap,
  throwError,
  timer,
} from 'rxjs';

import { environment } from '@environments/environment';
import { ApiClient } from '@core/api/services/api-client.service';
import { isApiClientClientError } from '@core/api/models/api-client-error.model';
import { ApiEnvelope, extractEnvelopeData } from '@core/api/models/api-envelope.model';
import { asRecord, readNullableString, readNumber, readString } from '@core/models/payload-casing';

/**
 * Where an upload is in validation. The API sends the backend enum as a number, so this list is
 * decoded by position and — like the enum — may only ever be appended to.
 */
export const MEDIA_ASSET_STATUSES = [
  'pendingUpload',
  'uploaded',
  'processing',
  'ready',
  'rejected',
  'needsReview',
] as const;

export type MediaAssetStatus = (typeof MEDIA_ASSET_STATUSES)[number];

/**
 * The error code of an attach that is not a refusal: the image is still being checked. The
 * upload is fine to keep; attach it again once {@link MediaAssetService.watch} reports it ready.
 */
export const MEDIA_PROCESSING_ERROR_CODE = 'MEDIA_PROCESSING';

export interface MediaAsset {
  id: string;
  status: MediaAssetStatus;
  /** The published image; set only once the asset is ready. */
  url: string | null;
  /** Why the upload was refused, once it has been. */
  rejectionReason: string | null;
}

/**
 * What {@link MediaAssetService.watch} reports: each status it reads, and `slow` once, when the
 * check has taken long enough that the editor should say so.
 */
export type MediaAssetWatchEvent = { kind: 'status'; asset: MediaAsset } | { kind: 'slow' };

/**
 * Poll cadence while waiting on a check: quickly at first, when the answer usually arrives, then
 * more slowly for the rest of the first minute. After that the editor says the image is still
 * processing, and reads are spaced out but keep going, so a late answer still swaps the image in.
 * Offsets are milliseconds after the first read.
 */
export const MEDIA_POLL_FAST_INTERVAL_MS = 1500;
export const MEDIA_POLL_FAST_WINDOW_MS = 10_000;
export const MEDIA_POLL_SLOW_INTERVAL_MS = 4000;
/** When the check counts as slow, and the reads move to the backed-off interval. */
export const MEDIA_POLL_SLOW_AFTER_MS = 60_000;
export const MEDIA_POLL_BACKOFF_INTERVAL_MS = 15_000;
/** When the watch stops for good. */
export const MEDIA_POLL_GIVE_UP_MS = 10 * 60_000;

function decodeStatus(value: number | undefined): MediaAssetStatus | null {
  return value !== undefined && Number.isInteger(value)
    ? (MEDIA_ASSET_STATUSES[value] ?? null)
    : null;
}

export function normalizeMediaAsset(payload: unknown): MediaAsset | null {
  const record = asRecord(payload);
  if (!record) return null;

  const id = readString(record, 'Id', 'id');
  const status = decodeStatus(readNumber(record, 'Status', 'status'));
  if (!id || !status) return null;

  return {
    id,
    status,
    url: readNullableString(record, 'Url', 'url') ?? null,
    rejectionReason: readNullableString(record, 'RejectionReason', 'rejectionReason') ?? null,
  };
}

/** Whether nothing more will happen to an asset in this status. */
export function isSettled(status: MediaAssetStatus): boolean {
  return status === 'ready' || status === 'rejected';
}

/**
 * The delay before the next read, given how long the watch has been running; null once it is
 * time to give up.
 */
export function nextPollDelay(elapsedMs: number): number | null {
  const interval =
    elapsedMs < MEDIA_POLL_FAST_WINDOW_MS
      ? MEDIA_POLL_FAST_INTERVAL_MS
      : elapsedMs < MEDIA_POLL_SLOW_AFTER_MS
        ? MEDIA_POLL_SLOW_INTERVAL_MS
        : MEDIA_POLL_BACKOFF_INTERVAL_MS;
  return elapsedMs + interval > MEDIA_POLL_GIVE_UP_MS ? null : interval;
}

type PollStep = { asset: MediaAsset | null; elapsedMs: number } | { slow: true };

/**
 * Reads the validation status of uploads, for editors waiting on an image they just attached.
 * Deliberately polling rather than a push channel: the wait is seconds long, and the editors are
 * not otherwise connected to a hub.
 */
@Injectable({ providedIn: 'root' })
export class MediaAssetService {
  private readonly api = inject(ApiClient);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly base = `${environment.backendUrl}/media`;

  getStatus(mediaAssetId: string): Observable<MediaAsset> {
    return this.api
      .get<ApiEnvelope<unknown>>(`${this.base}/${encodeURIComponent(mediaAssetId)}`)
      .pipe(
        map((response) => {
          const asset = normalizeMediaAsset(extractEnvelopeData(response));
          if (!asset) throw new Error('The image status could not be read.');
          return asset;
        }),
      );
  }

  /**
   * Reads the status now, then on the poll schedule until the asset is ready or rejected. Emits
   * `slow` once the check passes a minute, and keeps reading at a backed-off pace until it gives
   * up and completes. A failed read is skipped rather than ending the watch — except a 404,
   * which means the asset is gone or not the viewer's, and ends it quietly. Emits nothing during
   * server-side rendering, where there is no one to watch.
   */
  watch(mediaAssetId: string): Observable<MediaAssetWatchEvent> {
    if (!this.isBrowser) return EMPTY;

    const read = (elapsedMs: number): Observable<PollStep> =>
      this.getStatus(mediaAssetId).pipe(
        map((asset): PollStep => ({ asset, elapsedMs })),
        catchError((error: unknown) =>
          isApiClientClientError(error) && error.status === 404
            ? throwError(() => error)
            : of<PollStep>({ asset: null, elapsedMs }),
        ),
      );

    return read(0).pipe(
      expand((step) => {
        if ('slow' in step) return EMPTY;
        if (step.asset && isSettled(step.asset.status)) return EMPTY;

        const delay = nextPollDelay(step.elapsedMs);
        if (delay === null) return EMPTY;

        const next = step.elapsedMs + delay;
        const turnsSlow =
          step.elapsedMs < MEDIA_POLL_SLOW_AFTER_MS && next >= MEDIA_POLL_SLOW_AFTER_MS;
        return timer(delay).pipe(
          switchMap(() =>
            turnsSlow ? concat(of<PollStep>({ slow: true }), read(next)) : read(next),
          ),
        );
      }),
      filter((step) => 'slow' in step || step.asset !== null),
      map((step): MediaAssetWatchEvent =>
        'slow' in step ? { kind: 'slow' } : { kind: 'status', asset: step.asset! },
      ),
      catchError(() => EMPTY),
    );
  }
}
