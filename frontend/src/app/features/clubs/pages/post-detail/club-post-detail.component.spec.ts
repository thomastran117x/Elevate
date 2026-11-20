import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MockStore } from '@ngrx/store/testing';
import { Subject, of, throwError } from 'rxjs';

import {
  envelope,
  fakeActivatedRoute,
  makeClub,
  makeCurrentUser,
  provideTestStore,
} from '@testing';

import { ApiEnvelope } from '../../../../core/api/models/api-envelope.model';
import { selectUser } from '../../../../core/stores/user.selectors';
import { Club } from '../../models/club.types';
import { ClubPost } from '../../models/club-post.types';
import { ClubPostsService } from '../../services/club-posts.service';
import { ClubsService } from '../../services/clubs.service';
import { PostCommentsService } from '../../services/post-comments.service';
import { ClubPostDetailComponent } from './club-post-detail.component';

describe('ClubPostDetailComponent moderation access', () => {
  let component: ClubPostDetailComponent;
  let route: ReturnType<typeof fakeActivatedRoute>;
  let clubs: jasmine.SpyObj<ClubsService>;
  let store: MockStore;

  function setup(): void {
    route = fakeActivatedRoute({ params: { clubId: '1', postId: '5' } });

    const posts = jasmine.createSpyObj<ClubPostsService>('ClubPostsService', ['getPost']);
    posts.getPost.and.returnValue(of(envelope<ClubPost>(null)));
    clubs = jasmine.createSpyObj<ClubsService>('ClubsService', ['getClub']);

    TestBed.configureTestingModule({
      providers: [
        { provide: ActivatedRoute, useValue: route.route },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: ClubPostsService, useValue: posts },
        { provide: PostCommentsService, useValue: {} },
        { provide: ClubsService, useValue: clubs },
        ...provideTestStore({ user: makeCurrentUser({ Id: 7 }) }),
      ],
    });

    store = TestBed.inject(MockStore);
    // Only the class is under test; the template (and its comment thread) is not rendered.
    component = TestBed.runInInjectionContext(
      () =>
        new ClubPostDetailComponent(
          TestBed.inject(ActivatedRoute),
          TestBed.inject(Router),
          posts,
          TestBed.inject(PostCommentsService),
          clubs,
          store,
        ),
    );
  }

  afterEach(() => {
    component?.ngOnDestroy();
    TestBed.resetTestingModule();
  });

  it('lets club staff moderate', () => {
    setup();
    clubs.getClub.and.returnValue(of(envelope(makeClub({ id: 1, isVolunteer: true }))));

    component.ngOnInit();

    expect(clubs.getClub).toHaveBeenCalledOnceWith(1);
    expect(component.canModerate).toBeTrue();
  });

  it('does not let members moderate, or anyone when the club cannot be loaded', () => {
    setup();
    clubs.getClub.and.returnValue(of(envelope(makeClub({ id: 1 }))));
    component.ngOnInit();
    expect(component.canModerate).toBeFalse();

    clubs.getClub.and.returnValue(throwError(() => new Error('down')));
    route.setParams({ clubId: '2', postId: '6' });
    expect(component.canModerate).toBeFalse();
  });

  it('ignores a late answer for the club the viewer has already left', () => {
    setup();
    const clubA = new Subject<ApiEnvelope<Club>>();
    const clubB = new Subject<ApiEnvelope<Club>>();
    clubs.getClub.and.callFake((clubId: number) => (clubId === 1 ? clubA : clubB));

    component.ngOnInit();
    route.setParams({ clubId: '2', postId: '6' });
    clubB.next(envelope(makeClub({ id: 2 })));
    clubA.next(envelope(makeClub({ id: 1, isOwner: true })));

    expect(component.canModerate).toBeFalse();
  });

  it('does not refetch when the same user is emitted again, and resets on sign-out', () => {
    setup();
    clubs.getClub.and.returnValue(of(envelope(makeClub({ id: 1, isManager: true }))));
    component.ngOnInit();

    store.overrideSelector(selectUser, makeCurrentUser({ Id: 7, Name: 'Refreshed' }));
    store.refreshState();
    expect(clubs.getClub).toHaveBeenCalledTimes(1);

    store.overrideSelector(selectUser, null);
    store.refreshState();
    expect(component.canModerate).toBeFalse();
    expect(clubs.getClub).toHaveBeenCalledTimes(1);
  });
});
