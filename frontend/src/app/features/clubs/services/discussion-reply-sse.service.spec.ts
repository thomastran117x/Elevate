import { TestBed } from '@angular/core/testing';
import { Subscription } from 'rxjs';

import { environment } from '@environments/environment';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  DiscussionReplyLiveEvent,
  DiscussionReplySseService,
} from './discussion-reply-sse.service';

type Listener = (event: { data: string }) => void;

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly listeners = new Map<string, Listener[]>();
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;

  constructor(
    readonly url: string,
    readonly options?: EventSourceInit,
  ) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  emit(type: string, data: string): void {
    for (const listener of this.listeners.get(type) ?? []) listener({ data });
  }

  close(): void {
    this.closed = true;
  }
}

describe('DiscussionReplySseService', () => {
  let service: DiscussionReplySseService;
  let originalEventSource: typeof EventSource;
  let subscriptions: Subscription[];
  let authToken: { accessToken: string | null; refreshAccessToken: jasmine.Spy };
  let originalFetch: typeof fetch;

  beforeEach(() => {
    FakeEventSource.instances = [];
    subscriptions = [];
    authToken = { accessToken: null, refreshAccessToken: jasmine.createSpy('refreshAccessToken') };
    originalEventSource = window.EventSource;
    originalFetch = window.fetch;
    (window as { EventSource: unknown }).EventSource = FakeEventSource;
    TestBed.configureTestingModule({
      providers: [DiscussionReplySseService, { provide: AuthTokenService, useValue: authToken }],
    });
    service = TestBed.inject(DiscussionReplySseService);
  });

  afterEach(() => {
    subscriptions.forEach((subscription) => subscription.unsubscribe());
    (window as { EventSource: unknown }).EventSource = originalEventSource;
    window.fetch = originalFetch;
  });

  it('shares one credentialed club stream across subscribers', () => {
    subscriptions.push(service.connect(3).subscribe(), service.connect(3).subscribe());

    expect(FakeEventSource.instances.length).toBe(1);
    expect(FakeEventSource.instances[0].url).toBe(
      `${environment.backendUrl}/clubs/3/discussions/replies/events`,
    );
    expect(FakeEventSource.instances[0].options?.withCredentials).toBeTrue();
  });

  it('emits connection, reply, and aggregate reaction events', () => {
    const events: DiscussionReplyLiveEvent[] = [];
    subscriptions.push(service.connect(3).subscribe((event) => events.push(event)));
    const source = FakeEventSource.instances[0];

    source.onopen?.();
    source.emit('ReplyCreated', JSON.stringify({ Id: 5, DiscussionId: 7, Content: 'Live' }));
    source.emit(
      'ReplyReactionChanged',
      JSON.stringify({ discussionId: 7, replyId: 5, likeCount: 2, dislikeCount: 1 }),
    );

    expect(events[0]).toEqual({ type: 'Connected' });
    expect(events[1]).toEqual(
      jasmine.objectContaining({
        type: 'ReplyCreated',
        reply: jasmine.objectContaining({ id: 5 }),
      }),
    );
    expect(events[2]).toEqual({
      type: 'ReplyReactionChanged',
      discussionId: 7,
      replyId: 5,
      likeCount: 2,
      dislikeCount: 1,
    });
  });

  it('uses an Authorization header for a signed-in stream', async () => {
    authToken.accessToken = 'jwt-token';
    const body = new ReadableStream<Uint8Array>({ start: () => undefined });
    const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response(body, { status: 200 }));

    subscriptions.push(service.connect(4).subscribe());
    await Promise.resolve();

    expect(FakeEventSource.instances.length).toBe(0);
    const [, options] = fetchSpy.calls.mostRecent().args;
    expect(new Headers(options?.headers).get('Authorization')).toBe('Bearer jwt-token');
    expect(options?.credentials).toBe('include');
  });
});
