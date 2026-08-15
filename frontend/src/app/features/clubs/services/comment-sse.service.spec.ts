import { TestBed } from '@angular/core/testing';
import { Subscription } from 'rxjs';

import { environment } from '@environments/environment';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { SseCommentEvent, CommentSseService } from './comment-sse.service';

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

describe('CommentSseService', () => {
  let service: CommentSseService;
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
      providers: [CommentSseService, { provide: AuthTokenService, useValue: authToken }],
    });
    service = TestBed.inject(CommentSseService);
  });

  afterEach(() => {
    subscriptions.forEach((subscription) => subscription.unsubscribe());
    (window as { EventSource: unknown }).EventSource = originalEventSource;
    window.fetch = originalFetch;
  });

  it('shares one credentialed club stream across subscribers', () => {
    subscriptions.push(service.connect(3, 9).subscribe(), service.connect(3, 9).subscribe());

    expect(FakeEventSource.instances.length).toBe(1);
    expect(FakeEventSource.instances[0].url).toBe(
      `${environment.backendUrl}/clubs/3/posts/9/comments/events`,
    );
    expect(FakeEventSource.instances[0].options?.withCredentials).toBeTrue();
  });

  it('emits connection, reply, and aggregate reaction events', () => {
    const events: SseCommentEvent[] = [];
    subscriptions.push(service.connect(3, 9).subscribe((event) => events.push(event)));
    const source = FakeEventSource.instances[0];

    source.onopen?.();
    source.emit('CommentCreated', JSON.stringify({ Id: 5, PostId: 7, Content: 'Live' }));
    source.emit(
      'CommentReactionChanged',
      JSON.stringify({ postId: 7, commentId: 5, likeCount: 2, dislikeCount: 1 }),
    );

    expect(events[0]).toEqual({ type: 'Connected' });
    expect(events[1]).toEqual(
      jasmine.objectContaining({
        type: 'CommentCreated',
        comment: jasmine.objectContaining({ id: 5 }),
      }),
    );
    expect(events[2]).toEqual({
      type: 'CommentReactionChanged',
      postId: 7,
      commentId: 5,
      likeCount: 2,
      dislikeCount: 1,
    });
  });

  it('uses an Authorization header for a signed-in stream', async () => {
    authToken.accessToken = 'jwt-token';
    const body = new ReadableStream<Uint8Array>({ start: () => undefined });
    const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response(body, { status: 200 }));

    subscriptions.push(service.connect(4, 9).subscribe());
    await Promise.resolve();

    expect(FakeEventSource.instances.length).toBe(0);
    const [, options] = fetchSpy.calls.mostRecent().args;
    expect(new Headers(options?.headers).get('Authorization')).toBe('Bearer jwt-token');
    expect(options?.credentials).toBe('include');
  });

  it('refreshes once after 401 and parses chunked authenticated events', async () => {
    authToken.accessToken = 'expired-token';
    authToken.refreshAccessToken.and.resolveTo(undefined);
    const encoder = new TextEncoder();
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode('event: CommentUpdated\r\ndata: {"Id":5,"PostId":7,'));
        controller.enqueue(encoder.encode('"Content":"Updated"}\r\n\r\n'));
        controller.close();
      },
    });
    const fetchSpy = spyOn(window, 'fetch').and.returnValues(
      Promise.resolve(new Response(null, { status: 401 })),
      Promise.resolve(new Response(body, { status: 200 })),
    );
    const events: SseCommentEvent[] = [];

    const subscription = service.connect(5, 9).subscribe((event) => events.push(event));
    subscriptions.push(subscription);
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(authToken.refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(fetchSpy).toHaveBeenCalledTimes(2);
    expect(events).toContain(jasmine.objectContaining({ type: 'Connected' }));
    expect(events).toContain(
      jasmine.objectContaining({
        type: 'CommentUpdated',
        comment: jasmine.objectContaining({ id: 5, content: 'Updated' }),
      }),
    );
  });

  it('normalizes PascalCase reactions and ignores malformed events', () => {
    const events: SseCommentEvent[] = [];
    subscriptions.push(service.connect(6, 9).subscribe((event) => events.push(event)));
    const source = FakeEventSource.instances[0];

    source.emit(
      'CommentReactionChanged',
      JSON.stringify({ PostId: 7, CommentId: 5, LikeCount: 4, DislikeCount: 3 }),
    );
    source.emit('CommentDeleted', '{not-json');

    expect(events).toEqual([
      {
        type: 'CommentReactionChanged',
        postId: 7,
        commentId: 5,
        likeCount: 4,
        dislikeCount: 3,
      },
    ]);
  });

  it('closes the anonymous EventSource after the final subscriber leaves', () => {
    const first = service.connect(8, 9).subscribe();
    const second = service.connect(8, 9).subscribe();
    const source = FakeEventSource.instances[0];

    first.unsubscribe();
    expect(source.closed).toBeFalse();
    second.unsubscribe();

    expect(source.closed).toBeTrue();
  });
});
