import { NgZone, PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';

import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import {
  ClubRealtimeEvent,
  ClubRealtimeService,
  RealtimeConnectionState,
  RealtimePresence,
  RealtimePresenceUser,
} from './club-realtime.service';

type Handler = (payload: unknown) => void;

/** Stands in for a real `HubConnection`, letting specs drive the wire from the outside. */
class FakeHubConnection {
  state: HubConnectionState = HubConnectionState.Disconnected;
  readonly handlers = new Map<string, Handler[]>();
  readonly invocations: { method: string; args: unknown[] }[] = [];

  startCalls = 0;
  startRejects = false;
  invokeRejectsFor: string | null = null;

  private reconnectingCallbacks: (() => void)[] = [];
  private reconnectedCallbacks: (() => void)[] = [];
  private closeCallbacks: (() => void)[] = [];

  on(method: string, handler: Handler): void {
    const existing = this.handlers.get(method) ?? [];
    existing.push(handler);
    this.handlers.set(method, existing);
  }

  async start(): Promise<void> {
    this.startCalls += 1;
    if (this.startRejects) {
      throw new Error('connect failed');
    }
    this.state = HubConnectionState.Connected;
  }

  async stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected;
  }

  async invoke(method: string, ...args: unknown[]): Promise<void> {
    if (this.invokeRejectsFor === method) throw new Error(`${method} refused`);
    this.invocations.push({ method, args });
  }

  onreconnecting(callback: () => void): void {
    this.reconnectingCallbacks.push(callback);
  }
  onreconnected(callback: () => void): void {
    this.reconnectedCallbacks.push(callback);
  }
  onclose(callback: () => void): void {
    this.closeCallbacks.push(callback);
  }

  /** Test driver: deliver a server-to-client message. */
  emit(method: string, payload: unknown): void {
    for (const handler of this.handlers.get(method) ?? []) handler(payload);
  }

  fireReconnecting(): void {
    this.reconnectingCallbacks.forEach((callback) => callback());
  }
  async fireReconnected(): Promise<void> {
    this.state = HubConnectionState.Connected;
    for (const callback of this.reconnectedCallbacks) callback();
    await Promise.resolve();
    await Promise.resolve();
  }
  fireClose(): void {
    this.closeCallbacks.forEach((callback) => callback());
  }

  methodsInvoked(name: string): { method: string; args: unknown[] }[] {
    return this.invocations.filter((entry) => entry.method === name);
  }
}

describe('ClubRealtimeService', () => {
  let hub: FakeHubConnection;
  let service: ClubRealtimeService;
  let authToken: { accessToken: string | null };
  let withUrlOptions: { accessTokenFactory?: () => string } | undefined;

  function setup(platformId: string = 'browser'): void {
    hub = new FakeHubConnection();
    withUrlOptions = undefined;
    authToken = { accessToken: 'token-1' };

    spyOn(HubConnectionBuilder.prototype, 'withUrl').and.callFake(function (
      this: HubConnectionBuilder,
      _url: string,
      options: unknown,
    ) {
      withUrlOptions = options as { accessTokenFactory?: () => string };
      return this;
    } as never);
    spyOn(HubConnectionBuilder.prototype, 'withAutomaticReconnect').and.callFake(function (
      this: HubConnectionBuilder,
    ) {
      return this;
    } as never);
    spyOn(HubConnectionBuilder.prototype, 'configureLogging').and.callFake(function (
      this: HubConnectionBuilder,
    ) {
      return this;
    } as never);
    spyOn(HubConnectionBuilder.prototype, 'build').and.returnValue(hub as never);

    TestBed.configureTestingModule({
      providers: [
        ClubRealtimeService,
        { provide: AuthTokenService, useValue: authToken },
        { provide: PLATFORM_ID, useValue: platformId },
        { provide: NgZone, useValue: new NgZone({ enableLongStackTrace: false }) },
      ],
    });

    service = TestBed.inject(ClubRealtimeService);
  }

  /** Lets the promise chain inside joinThread/ensureStarted settle. */
  async function settle(): Promise<void> {
    for (let i = 0; i < 6; i++) await Promise.resolve();
  }

  it('does nothing on the server, so SSR never opens a socket', () => {
    setup('server');

    const emitted: ClubRealtimeEvent[] = [];
    service.events(1).subscribe((event) => emitted.push(event));
    service.joinThread(1, 'discussion', 9);

    expect(hub.startCalls).toBe(0);
    expect(emitted).toEqual([]);
  });

  it('reuses one connection for every thread in the same club', async () => {
    setup();

    service.joinThread(1, 'discussion', 9);
    service.joinThread(1, 'discussion', 10);
    await settle();

    expect(hub.startCalls).toBe(1);
    expect(hub.methodsInvoked('JoinClub').length).toBe(1);
    expect(hub.methodsInvoked('JoinDiscussion').length).toBe(2);
  });

  it('reads the access token on every connect so signing in later upgrades the socket', async () => {
    setup();
    service.joinThread(1, 'discussion', 9);
    await settle();

    expect(withUrlOptions?.accessTokenFactory?.()).toBe('token-1');

    authToken.accessToken = 'token-2';
    expect(withUrlOptions?.accessTokenFactory?.()).toBe('token-2');

    authToken.accessToken = null;
    expect(withUrlOptions?.accessTokenFactory?.()).toBe('');
  });

  it('maps reply and comment events into normalized payloads', async () => {
    setup();
    const emitted: ClubRealtimeEvent[] = [];
    service.events(1).subscribe((event) => emitted.push(event));
    service.joinThread(1, 'discussion', 9);
    await settle();

    hub.emit('ReplyCreated', { id: 5, discussionId: 9, content: 'Hi' });
    hub.emit('ReplyReactionChanged', {
      discussionId: 9,
      replyId: 5,
      likeCount: 3,
      dislikeCount: 1,
    });
    hub.emit('CommentCreated', { id: 7, postId: 4, content: 'Yo' });

    const created = emitted.find((event) => event.type === 'ReplyCreated');
    expect(created).toBeDefined();
    expect((created as { reply: { id: number } }).reply.id).toBe(5);

    const reaction = emitted.find((event) => event.type === 'ReplyReactionChanged');
    expect(reaction).toEqual({
      type: 'ReplyReactionChanged',
      discussionId: 9,
      replyId: 5,
      likeCount: 3,
      dislikeCount: 1,
    });

    const comment = emitted.find((event) => event.type === 'CommentCreated');
    expect((comment as { comment: { postId: number } }).comment.postId).toBe(4);
  });

  it('accepts PascalCase payloads as well as camelCase', async () => {
    setup();
    const emitted: ClubRealtimeEvent[] = [];
    service.events(1).subscribe((event) => emitted.push(event));
    service.joinThread(1, 'post', 4);
    await settle();

    hub.emit('CommentReactionChanged', {
      PostId: 4,
      CommentId: 8,
      LikeCount: 2,
      DislikeCount: 0,
    });

    expect(emitted.find((event) => event.type === 'CommentReactionChanged')).toEqual({
      type: 'CommentReactionChanged',
      postId: 4,
      commentId: 8,
      likeCount: 2,
      dislikeCount: 0,
    });
  });

  it('tracks connection state through reconnect and close', async () => {
    setup();
    const states: RealtimeConnectionState[] = [];
    service.connectionState(1).subscribe((state) => states.push(state));
    service.joinThread(1, 'discussion', 9);
    await settle();

    expect(states).toContain('live');

    hub.fireReconnecting();
    expect(states[states.length - 1]).toBe('reconnecting');

    await hub.fireReconnected();
    expect(states[states.length - 1]).toBe('live');

    hub.fireClose();
    expect(states[states.length - 1]).toBe('offline');
  });

  it('re-joins the club and every open thread after a reconnect, then asks for reconciliation', async () => {
    setup();
    const emitted: ClubRealtimeEvent[] = [];
    service.events(1).subscribe((event) => emitted.push(event));
    service.joinThread(1, 'discussion', 9);
    await settle();

    const joinsBefore = hub.methodsInvoked('JoinClub').length;
    await hub.fireReconnected();

    expect(hub.methodsInvoked('JoinClub').length).toBe(joinsBefore + 1);
    expect(hub.methodsInvoked('JoinDiscussion').length).toBe(2);
    expect(emitted.filter((event) => event.type === 'Connected').length).toBe(2);
  });

  it('reports offline when the first connect fails', async () => {
    setup();
    hub.startRejects = true;
    const states: RealtimeConnectionState[] = [];
    service.connectionState(1).subscribe((state) => states.push(state));

    service.joinThread(1, 'discussion', 9);
    await settle();

    expect(states[states.length - 1]).toBe('offline');
  });

  it('merges presence snapshots and diffs', async () => {
    setup();
    const seen: RealtimePresence[] = [];
    service.presence(1).subscribe((presence) => seen.push(presence));
    service.joinThread(1, 'discussion', 9);
    await settle();

    hub.emit('PresenceSnapshot', {
      clubId: 1,
      users: [{ userId: 7, name: 'Taylor', username: 'taylor', avatar: null }],
      totalOnline: 1,
    });
    expect(seen[seen.length - 1].totalOnline).toBe(1);

    hub.emit('PresenceChanged', {
      clubId: 1,
      joined: { userId: 8, name: 'Robin', username: 'robin', avatar: null },
      leftUserId: null,
      totalOnline: 2,
    });
    expect(seen[seen.length - 1].users.map((user) => user.userId)).toEqual([7, 8]);

    // A duplicate join (a second tab) must not double up the roster.
    hub.emit('PresenceChanged', {
      clubId: 1,
      joined: { userId: 8, name: 'Robin', username: 'robin', avatar: null },
      leftUserId: null,
      totalOnline: 2,
    });
    expect(seen[seen.length - 1].users.length).toBe(2);

    hub.emit('PresenceChanged', { clubId: 1, joined: null, leftUserId: 7, totalOnline: 1 });
    expect(seen[seen.length - 1].users.map((user) => user.userId)).toEqual([8]);
  });

  it('routes typing snapshots to the matching thread only', async () => {
    setup();
    const discussionTyping: RealtimePresenceUser[][] = [];
    const postTyping: RealtimePresenceUser[][] = [];
    service.typing(1, 'discussion', 9).subscribe((users) => discussionTyping.push(users));
    service.typing(1, 'post', 4).subscribe((users) => postTyping.push(users));
    service.joinThread(1, 'discussion', 9);
    await settle();

    hub.emit('TypingChanged', {
      threadKey: 'thread:discussion:9',
      users: [{ userId: 7, name: 'Taylor', username: 'taylor', avatar: null }],
    });

    expect(discussionTyping.length).toBe(1);
    expect(discussionTyping[0][0].userId).toBe(7);
    expect(postTyping.length).toBe(0);
  });

  it('refreshes typing on a timer and stops on demand', async () => {
    jasmine.clock().install();
    try {
      setup();
      service.joinThread(1, 'discussion', 9);
      await settle();

      service.setTyping(1, 'discussion', 9, true);
      await settle();
      expect(hub.methodsInvoked('Typing').length).toBe(1);

      jasmine.clock().tick(2100);
      await settle();
      expect(hub.methodsInvoked('Typing').length).toBe(2);

      service.setTyping(1, 'discussion', 9, false);
      await settle();
      const calls = hub.methodsInvoked('Typing');
      expect(calls[calls.length - 1].args[2]).toBe(false);

      // No further refreshes once stopped.
      const countAfterStop = hub.methodsInvoked('Typing').length;
      jasmine.clock().tick(5000);
      await settle();
      expect(hub.methodsInvoked('Typing').length).toBe(countAfterStop);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('does not broadcast typing for a thread it never joined', async () => {
    setup();
    service.joinThread(1, 'discussion', 9);
    await settle();

    service.setTyping(1, 'discussion', 99, true);
    await settle();

    expect(hub.methodsInvoked('Typing').length).toBe(0);
  });

  it('survives a refused join without breaking the connection', async () => {
    setup();
    hub.invokeRejectsFor = 'JoinDiscussion';
    const emitted: ClubRealtimeEvent[] = [];
    service.events(1).subscribe((event) => emitted.push(event));

    service.joinThread(1, 'discussion', 9);
    await settle();

    expect(emitted.some((event) => event.type === 'Connected')).toBeTrue();
    expect(hub.methodsInvoked('JoinClub').length).toBe(1);
  });

  it('leaves a discussion thread with the single-argument signature', async () => {
    setup();
    const leave = service.joinThread(1, 'discussion', 9);
    await settle();

    leave();
    await settle();

    expect(hub.methodsInvoked('LeaveDiscussion')[0].args).toEqual([9]);
  });

  it('ignores a presence diff that names neither a joiner nor a leaver', async () => {
    setup();
    const seen: RealtimePresence[] = [];
    service.presence(1).subscribe((presence) => seen.push(presence));
    service.joinThread(1, 'discussion', 9);
    await settle();

    hub.emit('PresenceChanged', { clubId: 1, joined: null, leftUserId: null, totalOnline: 4 });

    expect(seen[seen.length - 1].users).toEqual([]);
    expect(seen[seen.length - 1].totalOnline).toBe(4);
  });

  it('drops typing state for a thread once it is left', async () => {
    jasmine.clock().install();
    try {
      setup();
      const leave = service.joinThread(1, 'discussion', 9);
      await settle();

      service.setTyping(1, 'discussion', 9, true);
      await settle();
      const beforeLeave = hub.methodsInvoked('Typing').length;

      leave();
      await settle();

      jasmine.clock().tick(5000);
      await settle();
      expect(hub.methodsInvoked('Typing').length).toBe(beforeLeave);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('ignores typing for a club with no connection at all', () => {
    setup();

    expect(() => service.setTyping(42, 'discussion', 9, true)).not.toThrow();
  });

  it('tolerates a refused typing invocation', async () => {
    setup();
    hub.invokeRejectsFor = 'Typing';
    service.joinThread(1, 'discussion', 9);
    await settle();

    service.setTyping(1, 'discussion', 9, true);
    await settle();

    expect(hub.methodsInvoked('JoinClub').length).toBe(1);
  });

  it('tolerates a refused club join and still reports connected', async () => {
    setup();
    hub.invokeRejectsFor = 'JoinClub';
    const emitted: ClubRealtimeEvent[] = [];
    service.events(1).subscribe((event) => emitted.push(event));

    service.joinThread(1, 'discussion', 9);
    await settle();

    expect(emitted.some((event) => event.type === 'Connected')).toBeTrue();
  });

  it('returns no-op streams and teardown on the server', () => {
    setup('server');

    let stateSeen = false;
    let presenceSeen = false;
    let typingSeen = false;
    service.connectionState(1).subscribe(() => (stateSeen = true));
    service.presence(1).subscribe(() => (presenceSeen = true));
    service.typing(1, 'discussion', 9).subscribe(() => (typingSeen = true));

    const leave = service.joinThread(1, 'discussion', 9);
    expect(() => leave()).not.toThrow();
    expect(stateSeen).toBeFalse();
    expect(presenceSeen).toBeFalse();
    expect(typingSeen).toBeFalse();
  });

  it('does not send typing before the connection is up', () => {
    setup();
    service.setTyping(1, 'discussion', 9, false);

    expect(hub.methodsInvoked('Typing').length).toBe(0);
  });

  it('leaves the thread when the caller tears down', async () => {
    setup();
    const leave = service.joinThread(1, 'post', 4);
    await settle();

    leave();
    await settle();

    expect(hub.methodsInvoked('LeavePost').length).toBe(1);
    expect(hub.methodsInvoked('LeavePost')[0].args).toEqual([1, 4]);
  });
});
