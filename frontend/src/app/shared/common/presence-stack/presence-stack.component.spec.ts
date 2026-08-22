import { PresenceStackComponent, PresenceStackUser } from './presence-stack.component';

function user(id: number, overrides: Partial<PresenceStackUser> = {}): PresenceStackUser {
  return { userId: id, name: `User ${id}`, username: `user${id}`, avatar: null, ...overrides };
}

describe('PresenceStackComponent', () => {
  let component: PresenceStackComponent;

  beforeEach(() => {
    component = new PresenceStackComponent();
  });

  it('caps the visible avatars', () => {
    component.users = [1, 2, 3, 4, 5, 6, 7].map((id) => user(id));
    component.totalOnline = 7;

    expect(component.visible.length).toBe(component.maxVisible);
    expect(component.overflow).toBe(7 - component.maxVisible);
  });

  it('counts overflow against the server total, not the truncated roster', () => {
    // The server caps the roster it sends but still reports the true count.
    component.users = [user(1), user(2)];
    component.totalOnline = 120;

    expect(component.visible.length).toBe(2);
    expect(component.overflow).toBe(118);
  });

  it('never reports negative overflow', () => {
    component.users = [user(1), user(2)];
    component.totalOnline = 1;

    expect(component.overflow).toBe(0);
  });

  it('summarizes the count in words', () => {
    component.totalOnline = 0;
    expect(component.summary).toBe('No one else is here');

    component.totalOnline = 1;
    expect(component.summary).toBe('1 person online');

    component.totalOnline = 4;
    expect(component.summary).toBe('4 people online');
  });

  it('names a user through name, username, then id', () => {
    expect(component.displayName(user(1))).toBe('User 1');
    expect(component.displayName(user(1, { name: null }))).toBe('user1');
    expect(component.displayName(user(1, { name: null, username: null }))).toBe('User #1');
  });

  it('tracks by user id', () => {
    expect(component.trackByUserId(0, user(9))).toBe(9);
  });
});
