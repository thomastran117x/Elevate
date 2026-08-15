import { isPlatformBrowser } from '@angular/common';
import { Inject, Injectable, NgZone, PLATFORM_ID } from '@angular/core';
import { EMPTY, Observable, Subject, Subscriber, share } from 'rxjs';

import { environment } from '@environments/environment';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { DiscussionReply, normalizeDiscussionReply } from '../models/club-discussion.types';

export type DiscussionReplyLiveEvent =
  | { type: 'Connected' }
  | { type: 'ReplyCreated'; reply: DiscussionReply }
  | { type: 'ReplyUpdated'; reply: DiscussionReply }
  | { type: 'ReplyDeleted'; reply: DiscussionReply }
  | {
      type: 'ReplyReactionChanged';
      discussionId: number;
      replyId: number;
      likeCount: number;
      dislikeCount: number;
    };

@Injectable({ providedIn: 'root' })
export class DiscussionReplySseService {
  private readonly streams = new Map<number, Observable<DiscussionReplyLiveEvent>>();

  constructor(
    private zone: NgZone,
    private authToken: AuthTokenService,
    @Inject(PLATFORM_ID) private platformId: object,
  ) {}

  connect(clubId: number): Observable<DiscussionReplyLiveEvent> {
    if (!isPlatformBrowser(this.platformId)) return EMPTY;
    const existing = this.streams.get(clubId);
    if (existing) return existing;

    const url = `${environment.backendUrl}/clubs/${clubId}/discussions/replies/events`;
    const stream = new Observable<DiscussionReplyLiveEvent>((subscriber) =>
      this.authToken.accessToken
        ? this.openAuthenticatedStream(url, subscriber)
        : this.openAnonymousStream(url, subscriber),
    ).pipe(
      share({
        connector: () => new Subject<DiscussionReplyLiveEvent>(),
        resetOnError: true,
        resetOnComplete: true,
        resetOnRefCountZero: true,
      }),
    );

    this.streams.set(clubId, stream);
    return stream;
  }

  private openAnonymousStream(
    url: string,
    subscriber: Subscriber<DiscussionReplyLiveEvent>,
  ): () => void {
    const source = new EventSource(url, { withCredentials: true });
    source.onopen = () => this.zone.run(() => subscriber.next({ type: 'Connected' }));
    this.attachNativeListeners(source, subscriber);
    source.onerror = () => {
      // Native EventSource reconnects automatically.
    };
    return () => source.close();
  }

  private openAuthenticatedStream(
    url: string,
    subscriber: Subscriber<DiscussionReplyLiveEvent>,
  ): () => void {
    let stopped = false;
    let controller: AbortController | null = null;

    const reconnect = async (): Promise<void> => {
      while (!stopped) {
        controller = new AbortController();
        try {
          let response = await this.fetchStream(url, controller.signal);
          if (response.status === 401 && !stopped) {
            await this.authToken.refreshAccessToken();
            response = await this.fetchStream(url, controller.signal);
          }
          if (!response.ok || !response.body) {
            throw new Error(`SSE request failed (${response.status})`);
          }

          this.zone.run(() => subscriber.next({ type: 'Connected' }));
          await this.readEventStream(response.body, subscriber, controller.signal);
        } catch (error) {
          if (stopped || (error instanceof DOMException && error.name === 'AbortError')) return;
        }
        if (!stopped) await new Promise((resolve) => setTimeout(resolve, 1000));
      }
    };

    void reconnect();
    return () => {
      stopped = true;
      controller?.abort();
    };
  }

  private fetchStream(url: string, signal: AbortSignal): Promise<Response> {
    const headers = new Headers({ Accept: 'text/event-stream' });
    if (this.authToken.accessToken) {
      headers.set('Authorization', `Bearer ${this.authToken.accessToken}`);
    }
    return fetch(url, { headers, credentials: 'include', cache: 'no-store', signal });
  }

  private async readEventStream(
    body: ReadableStream<Uint8Array>,
    subscriber: Subscriber<DiscussionReplyLiveEvent>,
    signal: AbortSignal,
  ): Promise<void> {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    try {
      while (!signal.aborted) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
        let boundary = buffer.indexOf('\n\n');
        while (boundary >= 0) {
          const block = buffer.slice(0, boundary);
          buffer = buffer.slice(boundary + 2);
          this.consumeEventBlock(block, subscriber);
          boundary = buffer.indexOf('\n\n');
        }
      }
    } finally {
      reader.releaseLock();
    }
  }

  private consumeEventBlock(block: string, subscriber: Subscriber<DiscussionReplyLiveEvent>): void {
    let type = '';
    const data: string[] = [];
    for (const line of block.split('\n')) {
      if (line.startsWith('event:')) type = line.slice(6).trim();
      if (line.startsWith('data:')) data.push(line.slice(5).trimStart());
    }
    if (type && data.length > 0) this.emitParsed(type, data.join('\n'), subscriber);
  }

  private attachNativeListeners(
    source: EventSource,
    subscriber: Subscriber<DiscussionReplyLiveEvent>,
  ): void {
    for (const type of ['ReplyCreated', 'ReplyUpdated', 'ReplyDeleted', 'ReplyReactionChanged']) {
      source.addEventListener(type, (event: MessageEvent) =>
        this.emitParsed(type, event.data, subscriber),
      );
    }
  }

  private emitParsed(
    type: string,
    data: string,
    subscriber: Subscriber<DiscussionReplyLiveEvent>,
  ): void {
    this.zone.run(() => {
      try {
        if (type === 'ReplyCreated' || type === 'ReplyUpdated' || type === 'ReplyDeleted') {
          subscriber.next({ type, reply: normalizeDiscussionReply(JSON.parse(data)) });
          return;
        }
        if (type === 'ReplyReactionChanged') {
          const value = JSON.parse(data) as Record<string, number>;
          subscriber.next({
            type,
            discussionId: value['discussionId'] ?? value['DiscussionId'] ?? 0,
            replyId: value['replyId'] ?? value['ReplyId'] ?? 0,
            likeCount: value['likeCount'] ?? value['LikeCount'] ?? 0,
            dislikeCount: value['dislikeCount'] ?? value['DislikeCount'] ?? 0,
          });
        }
      } catch {
        // Ignore malformed events without terminating the live stream.
      }
    });
  }
}
