import {
  normalizeAuthor,
  normalizeClubPost,
  normalizeClubPostsPagedData,
  normalizePostComment,
  normalizePostCommentsPagedData,
  normalizePostType,
} from './club-post.types';

describe('normalizeAuthor', () => {
  it('reads both casings', () => {
    expect(normalizeAuthor({ Id: 3, Name: 'Jamie', Username: 'jamie', Avatar: null })).toEqual({
      id: 3,
      name: 'Jamie',
      username: 'jamie',
      avatar: null,
    });

    expect(normalizeAuthor({ id: 4 })?.id).toBe(4);
  });

  it('returns null for a missing author', () => {
    expect(normalizeAuthor(null)).toBeNull();
    expect(normalizeAuthor(undefined)).toBeNull();
  });
});

describe('normalizePostType', () => {
  it('maps a numeric enum to its label', () => {
    expect(normalizePostType(0)).toBe('General');
    expect(normalizePostType(1)).toBe('Announcement');
    expect(normalizePostType(2)).toBe('Event');
    expect(normalizePostType(3)).toBe('Poll');
  });

  it('falls back to General for an out-of-range number', () => {
    expect(normalizePostType(99)).toBe('General');
  });

  it('passes a known string through and rejects an unknown one', () => {
    expect(normalizePostType('Poll')).toBe('Poll');
    expect(normalizePostType('Rumour')).toBe('General');
    expect(normalizePostType(undefined)).toBe('General');
  });
});

describe('normalizeClubPost', () => {
  it('reads a PascalCase payload including the nested author', () => {
    expect(
      normalizeClubPost({
        Id: 1,
        ClubId: 2,
        UserId: 3,
        Title: 'Kickoff',
        Content: 'Welcome',
        PostType: 1,
        LikesCount: 7,
        ViewCount: 42,
        IsPinned: true,
        Author: { Id: 3, Name: 'Jamie' },
        CreatedAt: '2026-01-01T00:00:00Z',
        UpdatedAt: '2026-01-02T00:00:00Z',
      }),
    ).toEqual({
      id: 1,
      clubId: 2,
      userId: 3,
      title: 'Kickoff',
      content: 'Welcome',
      postType: 'Announcement',
      likesCount: 7,
      viewCount: 42,
      isPinned: true,
      author: { id: 3, name: 'Jamie', username: null, avatar: null },
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-02T00:00:00Z',
    });
  });

  it('defaults an empty payload and leaves the author null', () => {
    const result = normalizeClubPost({});

    expect(result.id).toBe(0);
    expect(result.title).toBe('');
    expect(result.postType).toBe('General');
    expect(result.isPinned).toBeFalse();
    expect(result.author).toBeNull();
  });
});

describe('normalizePostComment', () => {
  it('reads both casings and normalizes the author', () => {
    expect(
      normalizePostComment({
        Id: 5,
        PostId: 6,
        UserId: 7,
        Content: 'Nice',
        Author: { Id: 7, Username: 'jamie' },
        CreatedAt: '2026-01-03T00:00:00Z',
        UpdatedAt: '2026-01-03T00:00:00Z',
      }),
    ).toEqual({
      id: 5,
      postId: 6,
      userId: 7,
      content: 'Nice',
      author: { id: 7, name: null, username: 'jamie', avatar: null },
      createdAt: '2026-01-03T00:00:00Z',
      updatedAt: '2026-01-03T00:00:00Z',
    });
  });
});

describe('paged post normalizers', () => {
  it('maps posts and applies the paging defaults', () => {
    const result = normalizeClubPostsPagedData({ Items: [{ Id: 1 }], TotalCount: 1 });

    expect(result.items[0].id).toBe(1);
    expect(result.totalCount).toBe(1);
    expect(result.page).toBe(1);
    expect(result.pageSize).toBe(20);
    expect(result.totalPages).toBe(0);
  });

  it('maps comments and honours explicit paging metadata', () => {
    const result = normalizePostCommentsPagedData({
      items: [{ id: 9 }],
      page: 2,
      pageSize: 5,
      totalPages: 3,
      totalCount: 11,
    });

    expect(result.items[0].id).toBe(9);
    expect(result).toEqual(
      jasmine.objectContaining({ page: 2, pageSize: 5, totalPages: 3, totalCount: 11 }),
    );
  });

  it('returns an empty list when neither items key is present', () => {
    expect(normalizeClubPostsPagedData({}).items).toEqual([]);
    expect(normalizePostCommentsPagedData({}).items).toEqual([]);
  });
});
