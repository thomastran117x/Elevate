import { TestBed } from '@angular/core/testing';
import { Subscription } from 'rxjs';

import { environment } from '@environments/environment';

import { CommentSseService, SseCommentEvent } from './comment-sse.service';

type Listener = (event: { data: string }) => void;

class FakeEventSource {
  static instances: FakeEventSource[] = [];

  readonly listeners = new Map<string, Listener[]>();
  onerror: (() => void) | null = null;
  closed = false;

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  emit(type: string, data: string): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ data });
    }
  }

  close(): void {
    this.closed = true;
  }
}

describe('CommentSseService', () => {
  let service: CommentSseService;
  let events: SseCommentEvent[];
  let subscription: Subscription;
  let originalEventSource: typeof EventSource;

  beforeEach(() => {
    FakeEventSource.instances = [];
    originalEventSource = window.EventSource;
    (window as { EventSource: unknown }).EventSource = FakeEventSource;

    TestBed.configureTestingModule({ providers: [CommentSseService] });
    service = TestBed.inject(CommentSseService);

    events = [];
    subscription = service.connect(3, 9).subscribe((event) => events.push(event));
  });

  afterEach(() => {
    subscription.unsubscribe();
    (window as { EventSource: unknown }).EventSource = originalEventSource;
  });

  function source(): FakeEventSource {
    return FakeEventSource.instances[0];
  }

  it('opens a stream against the post comments endpoint', () => {
    expect(FakeEventSource.instances.length).toBe(1);
    expect(source().url).toBe(`${environment.backendUrl}/clubs/3/posts/9/comments/events`);
  });

  it('emits a normalized comment for CommentCreated', () => {
    source().emit('CommentCreated', JSON.stringify({ Id: 1, Content: 'Nice', PostId: 9 }));

    expect(events.length).toBe(1);
    expect(events[0].type).toBe('CommentCreated');
    expect((events[0] as Extract<SseCommentEvent, { type: 'CommentCreated' }>).comment).toEqual(
      jasmine.objectContaining({ id: 1, content: 'Nice', postId: 9 }),
    );
  });

  it('emits a normalized comment for CommentUpdated', () => {
    source().emit('CommentUpdated', JSON.stringify({ id: 1, content: 'Edited' }));

    expect(events.length).toBe(1);
    expect(events[0].type).toBe('CommentUpdated');
  });

  it('emits the identifiers for CommentDeleted', () => {
    source().emit('CommentDeleted', JSON.stringify({ postId: 9, commentId: 5 }));

    expect(events).toEqual([{ type: 'CommentDeleted', postId: 9, commentId: 5 }]);
  });

  it('drops a malformed event without ending the stream', () => {
    source().emit('CommentCreated', 'not-json');
    source().emit('CommentCreated', JSON.stringify({ Id: 2, Content: 'Recovered' }));

    expect(events.length).toBe(1);
    expect((events[0] as { comment: { content: string } }).comment.content).toBe('Recovered');
  });

  it('ignores transient stream errors', () => {
    expect(() => source().onerror?.()).not.toThrow();
  });

  it('closes the stream when the subscriber unsubscribes', () => {
    expect(source().closed).toBeFalse();

    subscription.unsubscribe();

    expect(source().closed).toBeTrue();
  });
});
