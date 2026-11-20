import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
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
import { ClubReview } from '../../../models/club-review.types';
import { ClubReviewsService } from '../../../services/club-reviews.service';
import { ClubsService } from '../../../services/clubs.service';
import { ReviewsTabComponent } from './reviews-tab.component';

function makeReview(overrides: Partial<ClubReview> = {}): ClubReview {
  return {
    id: 1,
    userId: 5,
    clubId: 3,
    title: 'Terrible',
    rating: 1,
    comment: 'Abusive review',
    createdAt: '2026-11-13T00:00:00Z',
    name: 'Ava Thompson',
    username: 'ava',
    usernameDisplay: 'ava',
    avatar: null,
    ...overrides,
  };
}

describe('ReviewsTabComponent', () => {
  let fixture: ComponentFixture<ReviewsTabComponent>;
  let component: ReviewsTabComponent;
  let reviews: jasmine.SpyObj<ClubReviewsService>;
  let clubs: jasmine.SpyObj<ClubsService>;

  async function setup(club: Partial<Club>): Promise<void> {
    reviews = jasmine.createSpyObj<ClubReviewsService>('ClubReviewsService', [
      'getReviews',
      'deleteReview',
    ]);
    reviews.getReviews.and.returnValue(
      of(envelope({ items: [makeReview()], totalCount: 1, page: 1, pageSize: 20, totalPages: 1 })),
    );
    reviews.deleteReview.and.returnValue(of(envelope(null)));

    clubs = jasmine.createSpyObj<ClubsService>('ClubsService', ['getClub']);
    clubs.getClub.and.returnValue(of(envelope(makeClub({ id: 3, ...club }))));

    const parent = fakeActivatedRoute({ params: { clubId: '3' } }).route;

    await TestBed.configureTestingModule({
      imports: [ReviewsTabComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { parent } },
        { provide: ClubReviewsService, useValue: reviews },
        { provide: ClubsService, useValue: clubs },
        ...provideTestStore({ user: makeCurrentUser({ Id: 1 }) }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('offers removal of other people’s reviews to staff only', async () => {
    await setup({ isVolunteer: true });

    expect(component.canRemoveReview(makeReview({ userId: 5 }))).toBeTrue();
    expect(component.canRemoveReview(makeReview({ userId: 1 }))).toBeFalse();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Remove');
  });

  it('hides removal from members', async () => {
    await setup({});

    expect(component.canRemoveReview(makeReview({ userId: 5 }))).toBeFalse();
  });

  it('removes a review with the reason and reloads', async () => {
    await setup({ isManager: true });
    component.openRemoveReview(makeReview({ id: 9 }));
    component.reviewRemovalReason = ' Abusive ';

    component.resolveRemoveReview(true);

    expect(reviews.deleteReview).toHaveBeenCalledOnceWith(3, 9, 'Abusive');
    expect(component.success).toBe('Review removed.');
    expect(component.removingReview).toBeNull();
    expect(reviews.getReviews).toHaveBeenCalledTimes(2);
  });

  it('keeps the review when the dialog is dismissed', async () => {
    await setup({ isManager: true });
    component.openRemoveReview(makeReview({ id: 9 }));

    component.resolveRemoveReview(false);

    expect(reviews.deleteReview).not.toHaveBeenCalled();
    expect(component.removingReview).toBeNull();
  });

  it('shows the server’s refusal', async () => {
    await setup({ isVolunteer: true });
    reviews.deleteReview.and.returnValue(
      throwError(
        () =>
          new ApiClientClientError('You are not allowed to delete this review.', 403, 'FORBIDDEN'),
      ),
    );
    component.openRemoveReview(makeReview({ id: 9 }));
    component.reviewRemovalReason = 'Spam';

    component.resolveRemoveReview(true);

    expect(component.error).toBe('You are not allowed to delete this review.');
    expect(component.reviewRemovalBusy).toBeFalse();
  });
});
