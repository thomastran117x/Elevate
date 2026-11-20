import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';

import {
  envelope,
  fakeActivatedRoute,
  makeClub,
  makeCurrentUser,
  provideTestStore,
} from '@testing';

import { ApiClientClientError } from '../../../../../core/api/models/api-client-error.model';
import { Club } from '../../../models/club.types';
import { ClubPost } from '../../../models/club-post.types';
import { ClubPostsService } from '../../../services/club-posts.service';
import { ClubsService } from '../../../services/clubs.service';
import { PostsTabComponent } from './posts-tab.component';

function makePost(overrides: Partial<ClubPost> = {}): ClubPost {
  return {
    id: 1,
    clubId: 3,
    userId: 1,
    title: 'Club news',
    content: 'Body',
    postType: 'General',
    likesCount: 0,
    viewCount: 0,
    commentCount: 0,
    isPinned: false,
    author: null,
    createdAt: '2026-11-13T00:00:00Z',
    updatedAt: '2026-11-13T00:00:00Z',
    ...overrides,
  };
}

describe('PostsTabComponent', () => {
  let fixture: ComponentFixture<PostsTabComponent>;
  let component: PostsTabComponent;
  let posts: jasmine.SpyObj<ClubPostsService>;
  let clubs: jasmine.SpyObj<ClubsService>;

  async function setup(club: Partial<Club> | null): Promise<void> {
    posts = jasmine.createSpyObj<ClubPostsService>('ClubPostsService', [
      'getPosts',
      'createPost',
      'updatePost',
      'deletePost',
    ]);
    posts.getPosts.and.returnValue(
      of(
        envelope({
          items: [makePost({ id: 1, userId: 1 }), makePost({ id: 2, userId: 9 })],
          totalCount: 2,
          page: 1,
          pageSize: 10,
          totalPages: 1,
        }),
      ),
    );
    posts.deletePost.and.returnValue(of(envelope(null)));

    clubs = jasmine.createSpyObj<ClubsService>('ClubsService', ['getClub']);
    clubs.getClub.and.returnValue(
      club ? of(envelope(makeClub({ id: 3, ...club }))) : throwError(() => new Error('down')),
    );

    const parent = fakeActivatedRoute({ params: { clubId: '3' } }).route;

    await TestBed.configureTestingModule({
      imports: [PostsTabComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { parent } },
        { provide: ClubPostsService, useValue: posts },
        { provide: ClubsService, useValue: clubs },
        ...provideTestStore({ user: makeCurrentUser({ Id: 1 }) }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PostsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  function buttonLabels(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(
      (button) => button.textContent?.trim() ?? '',
    );
  }

  it('lets a volunteer change only their own posts, and never pin', async () => {
    await setup({ isVolunteer: true });

    expect(component.canManagePosts).toBeFalse();
    expect(component.canChange(makePost({ userId: 1 }))).toBeTrue();
    expect(component.canChange(makePost({ userId: 9 }))).toBeFalse();
    expect(buttonLabels()).not.toContain('Pin');
    expect(buttonLabels().filter((label) => label === 'Edit').length).toBe(1);
  });

  it('falls back to the volunteer view when the club cannot be loaded', async () => {
    await setup(null);

    expect(component.club).toBeNull();
    expect(component.canManagePosts).toBeFalse();
  });

  it('lets managers pin and change every post', async () => {
    await setup({ isManager: true, canManage: true });

    expect(component.canManagePosts).toBeTrue();
    expect(component.canChange(makePost({ userId: 9 }))).toBeTrue();
    expect(buttonLabels()).toContain('Pin');
    expect(buttonLabels()).toContain('Remove');
  });

  it('deletes the viewer’s own post straight away', async () => {
    await setup({ isVolunteer: true });

    component.requestDelete(makePost({ id: 1, userId: 1 }));

    expect(posts.deletePost).toHaveBeenCalledOnceWith(3, 1, undefined);
    expect(component.success).toBe('Post deleted.');
    expect(component.removingPost).toBeNull();
  });

  it('asks a manager for a reason before removing someone else’s post', async () => {
    await setup({ isManager: true, canManage: true });

    component.requestDelete(makePost({ id: 2, userId: 9 }));
    expect(component.removingPost?.id).toBe(2);
    expect(posts.deletePost).not.toHaveBeenCalled();

    component.postRemovalReason = ' Outdated ';
    component.resolveRemovePost(true);

    expect(posts.deletePost).toHaveBeenCalledOnceWith(3, 2, 'Outdated');
    expect(component.success).toBe('Post removed.');
    expect(component.posts.map((post) => post.id)).toEqual([1]);
  });

  it('keeps the post when the removal is dismissed', async () => {
    await setup({ isManager: true, canManage: true });
    component.requestDelete(makePost({ id: 2, userId: 9 }));

    component.resolveRemovePost(false);

    expect(posts.deletePost).not.toHaveBeenCalled();
    expect(component.removingPost).toBeNull();
  });

  it('shows the server’s refusal', async () => {
    await setup({ isManager: true, canManage: true });
    posts.deletePost.and.returnValue(
      throwError(
        () =>
          new ApiClientClientError('You are not allowed to delete this post.', 403, 'FORBIDDEN'),
      ),
    );
    component.requestDelete(makePost({ id: 2, userId: 9 }));
    component.postRemovalReason = 'Outdated';

    component.resolveRemovePost(true);

    expect(component.error).toBe('You are not allowed to delete this post.');
  });
});
