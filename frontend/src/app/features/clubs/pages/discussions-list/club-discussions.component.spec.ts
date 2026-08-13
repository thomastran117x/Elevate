import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import {
  envelope,
  fakeActivatedRoute,
  makeClubDiscussion,
  makeCurrentUser,
  provideTestStore,
} from '@testing';

import { ClubDiscussionsComponent } from './club-discussions.component';
import { ClubDiscussionsService } from '../../services/club-discussions.service';
import { ClubsService } from '../../services/clubs.service';
import { ClubDiscussion } from '../../models/club-discussion.types';
import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';
import { User } from '../../../../core/stores/user.model';

interface Paged<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

function paged(items: ClubDiscussion[], totalCount = items.length, totalPages = 1) {
  return envelope<Paged<ClubDiscussion>>({
    items,
    totalCount,
    page: 1,
    pageSize: 20,
    totalPages,
  });
}

describe('ClubDiscussionsComponent', () => {
  let fixture: ComponentFixture<ClubDiscussionsComponent>;
  let component: ClubDiscussionsComponent;
  let route: ReturnType<typeof fakeActivatedRoute>;
  let router: jasmine.SpyObj<Router>;
  let discussions: jasmine.SpyObj<ClubDiscussionsService>;
  let clubs: jasmine.SpyObj<ClubsService>;

  async function setup(
    user: User | null = makeCurrentUser({ Id: 1 }),
    isMember = true,
  ): Promise<void> {
    route = fakeActivatedRoute({ params: { clubId: '3' } });

    router = jasmine.createSpyObj<Router>('Router', ['navigate'], { url: '/clubs/3/discussions' });
    router.navigate.and.resolveTo(true);

    discussions = jasmine.createSpyObj<ClubDiscussionsService>('ClubDiscussionsService', [
      'getDiscussions',
      'createDiscussion',
      'updateDiscussion',
      'deleteDiscussion',
    ]);
    discussions.getDiscussions.and.returnValue(of(paged([])));
    discussions.createDiscussion.and.returnValue(of(envelope(makeClubDiscussion())));
    discussions.updateDiscussion.and.returnValue(of(envelope(makeClubDiscussion())));
    discussions.deleteDiscussion.and.returnValue(of(envelope(null)));

    clubs = jasmine.createSpyObj<ClubsService>('ClubsService', ['getMembershipStatus']);
    clubs.getMembershipStatus.and.returnValue(of(isMember));

    await TestBed.configureTestingModule({
      imports: [ClubDiscussionsComponent],
      providers: [
        { provide: ActivatedRoute, useValue: route.route },
        { provide: Router, useValue: router },
        { provide: ClubDiscussionsService, useValue: discussions },
        { provide: ClubsService, useValue: clubs },
        ...provideTestStore({ user }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ClubDiscussionsComponent);
    component = fixture.componentInstance;
  }

  beforeEach(async () => {
    await setup();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  describe('initial load', () => {
    it('loads the first page for the routed club', () => {
      discussions.getDiscussions.and.returnValue(
        of(paged([makeClubDiscussion({ id: 2 }), makeClubDiscussion({ id: 1 })], 2)),
      );

      fixture.detectChanges();

      expect(component.clubId).toBe(3);
      expect(discussions.getDiscussions).toHaveBeenCalledWith(3, 1, 20);
      expect(component.discussions.map((d) => d.id)).toEqual([2, 1]);
      expect(component.totalCount).toBe(2);
      expect(component.loading).toBeFalse();
    });

    it('surfaces a load failure', () => {
      discussions.getDiscussions.and.returnValue(
        throwError(() => new ApiClientClientError('Members only.', 403, 'FORBIDDEN')),
      );

      fixture.detectChanges();

      expect(component.error).toBe('Members only.');
      expect(component.loading).toBeFalse();
    });

    it('skips fetching when the club id is missing', async () => {
      TestBed.resetTestingModule();
      await setup();
      route.setParams({ clubId: 'not-a-number' });

      fixture.detectChanges();

      expect(component.clubId).toBe(0);
      expect(discussions.getDiscussions).not.toHaveBeenCalled();
    });
  });

  describe('permission to start a discussion', () => {
    it('allows a signed-in member', () => {
      fixture.detectChanges();

      expect(clubs.getMembershipStatus).toHaveBeenCalledWith(3);
      expect(component.isMember).toBeTrue();
      expect(component.canStartDiscussion).toBeTrue();
    });

    it('blocks a signed-in non-member', async () => {
      TestBed.resetTestingModule();
      await setup(makeCurrentUser({ Id: 1 }), false);

      fixture.detectChanges();

      expect(component.canStartDiscussion).toBeFalse();
    });

    it('blocks an anonymous visitor without checking membership', async () => {
      TestBed.resetTestingModule();
      await setup(null);

      fixture.detectChanges();

      expect(clubs.getMembershipStatus).not.toHaveBeenCalled();
      expect(component.canStartDiscussion).toBeFalse();
    });

    it('treats a failed membership check as not a member', async () => {
      TestBed.resetTestingModule();
      await setup();
      clubs.getMembershipStatus.and.returnValue(throwError(() => new Error('boom')));

      fixture.detectChanges();

      expect(component.isMember).toBeFalse();
    });
  });

  describe('creating and editing', () => {
    beforeEach(() => fixture.detectChanges());

    it('opens a blank editor for a new discussion', () => {
      component.openCreate();

      expect(component.showEditor).toBeTrue();
      expect(component.editingId).toBeNull();
      expect(component.form).toEqual({ title: '', description: '' });
    });

    it('prefills the editor when editing', () => {
      component.openEdit(makeClubDiscussion({ id: 5, title: 'A', description: 'B' }));

      expect(component.editingId).toBe(5);
      expect(component.form).toEqual({ title: 'A', description: 'B' });
    });

    it('requires both fields before submitting', () => {
      component.openCreate();
      component.form = { title: '  ', description: 'Body' };

      component.saveDiscussion();

      expect(component.error).toBe('Title and description are required.');
      expect(discussions.createDiscussion).not.toHaveBeenCalled();
    });

    it('creates a discussion and reloads the list', () => {
      component.openCreate();
      component.form = { title: '  Weekend ride  ', description: '  Where?  ' };

      component.saveDiscussion();

      expect(discussions.createDiscussion).toHaveBeenCalledWith(3, {
        title: 'Weekend ride',
        description: 'Where?',
      });
      expect(component.success).toBe('Discussion started.');
      expect(component.showEditor).toBeFalse();
      expect(component.saving).toBeFalse();
      expect(discussions.getDiscussions).toHaveBeenCalledTimes(2);
    });

    it('updates an existing discussion', () => {
      component.openEdit(makeClubDiscussion({ id: 5, title: 'A', description: 'B' }));
      component.form = { title: 'A2', description: 'B2' };

      component.saveDiscussion();

      expect(discussions.updateDiscussion).toHaveBeenCalledWith(3, 5, {
        title: 'A2',
        description: 'B2',
      });
      expect(component.success).toBe('Discussion updated.');
    });

    it('surfaces a save failure and keeps the editor open', () => {
      discussions.createDiscussion.and.returnValue(
        throwError(() => new ApiClientClientError('You must be a member.', 403, 'FORBIDDEN')),
      );

      component.openCreate();
      component.form = { title: 'T', description: 'D' };
      component.saveDiscussion();

      expect(component.error).toBe('You must be a member.');
      expect(component.showEditor).toBeTrue();
      expect(component.saving).toBeFalse();
    });

    it('clears the editor on cancel', () => {
      component.openEdit(makeClubDiscussion({ id: 5 }));

      component.cancelEditor();

      expect(component.showEditor).toBeFalse();
      expect(component.editingId).toBeNull();
      expect(component.form).toEqual({ title: '', description: '' });
    });
  });

  describe('deleting', () => {
    beforeEach(() => fixture.detectChanges());

    it('deletes a discussion and reloads', () => {
      component.deleteDiscussion(makeClubDiscussion({ id: 5 }));

      expect(discussions.deleteDiscussion).toHaveBeenCalledWith(3, 5);
      expect(component.success).toBe('Discussion deleted.');
      expect(discussions.getDiscussions).toHaveBeenCalledTimes(2);
    });

    it('surfaces a delete failure', () => {
      discussions.deleteDiscussion.and.returnValue(
        throwError(() => new ApiClientClientError('Not yours.', 403, 'FORBIDDEN')),
      );

      component.deleteDiscussion(makeClubDiscussion({ id: 5 }));

      expect(component.error).toBe('Not yours.');
    });
  });

  describe('paging', () => {
    it('appends the next page and ignores a request past the last page', () => {
      discussions.getDiscussions.and.returnValue(of(paged([makeClubDiscussion({ id: 1 })], 2, 2)));
      fixture.detectChanges();

      discussions.getDiscussions.and.returnValue(of(paged([makeClubDiscussion({ id: 2 })], 2, 2)));
      component.loadMore();

      expect(discussions.getDiscussions).toHaveBeenCalledWith(3, 2, 20);
      expect(component.discussions.map((d) => d.id)).toEqual([1, 2]);
      expect(component.loadingMore).toBeFalse();

      discussions.getDiscussions.calls.reset();
      component.loadMore();
      expect(discussions.getDiscussions).not.toHaveBeenCalled();
    });

    it('reports a failure while appending', () => {
      discussions.getDiscussions.and.returnValue(of(paged([makeClubDiscussion({ id: 1 })], 2, 2)));
      fixture.detectChanges();

      discussions.getDiscussions.and.returnValue(
        throwError(() => new ApiClientClientError('Nope.', 500)),
      );
      component.loadMore();

      expect(component.error).toBe('Nope.');
      expect(component.loadingMore).toBeFalse();
    });
  });

  describe('display helpers', () => {
    beforeEach(() => fixture.detectChanges());

    it('marks only the current user as the author', () => {
      expect(component.isAuthor(makeClubDiscussion({ userId: 1 }))).toBeTrue();
      expect(component.isAuthor(makeClubDiscussion({ userId: 2 }))).toBeFalse();
    });

    it('renders the author name and the created date', () => {
      expect(component.authorDisplay(makeClubDiscussion({ userId: 2 }))).toBe('Jamie Rivers');
      expect(component.formatDate('2026-08-01T12:00:00Z')).toContain('2026');
    });

    it('navigates back to the club', () => {
      component.goBack();

      expect(router.navigate).toHaveBeenCalledWith(['/clubs', 3]);
    });
  });
});
