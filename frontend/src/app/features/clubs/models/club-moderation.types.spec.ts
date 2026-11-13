import {
  normalizeClubModerationLogItem,
  normalizeClubModerationLogPage,
} from './club-moderation.types';

describe('club moderation log normalizers', () => {
  it('reads a PascalCase entry', () => {
    expect(
      normalizeClubModerationLogItem({
        Id: 4,
        ClubId: 2,
        Action: 'Remove',
        ContentType: 'Comment',
        ContentId: 30,
        ContentSnapshot: 'Buy cheap watches',
        Reason: 'Spam',
        ActorClubRole: 'Volunteer',
        Actor: { Id: 7, Name: 'Noah Patel', Username: 'noah' },
        Author: { Id: 8, Username: 'ava', Avatar: 'a.png' },
        CreatedAt: '2026-11-13T10:00:00Z',
      }),
    ).toEqual({
      id: 4,
      clubId: 2,
      action: 'remove',
      contentType: 'Comment',
      contentId: 30,
      contentSnapshot: 'Buy cheap watches',
      reason: 'Spam',
      actorClubRole: 'Volunteer',
      actor: { id: 7, name: 'Noah Patel', username: 'noah', usernameDisplay: 'noah', avatar: null },
      author: { id: 8, name: null, username: 'ava', usernameDisplay: 'ava', avatar: 'a.png' },
      createdAt: '2026-11-13T10:00:00Z',
    });
  });

  it('falls back to safe defaults for a sparse entry', () => {
    const item = normalizeClubModerationLogItem({ action: 'mystery' });

    expect(item.action).toBe('remove');
    expect(item.reason).toBeNull();
    expect(item.contentSnapshot).toBeNull();
    expect(item.actor).toEqual({
      id: 0,
      name: null,
      username: null,
      usernameDisplay: null,
      avatar: null,
    });
  });

  it('keeps known actions', () => {
    expect(normalizeClubModerationLogItem({ action: 'unpin' }).action).toBe('unpin');
  });

  it('reads a camelCase page and defaults a missing one', () => {
    const page = normalizeClubModerationLogPage({
      items: [{ id: 1, action: 'pin', contentType: 'Post' }],
      totalCount: 21,
      page: 2,
      pageSize: 10,
      totalPages: 3,
    });

    expect(page.items.map((item) => item.action)).toEqual(['pin']);
    expect(page.totalCount).toBe(21);
    expect(page.page).toBe(2);
    expect(page.totalPages).toBe(3);

    expect(normalizeClubModerationLogPage({})).toEqual({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
    });
  });
});
