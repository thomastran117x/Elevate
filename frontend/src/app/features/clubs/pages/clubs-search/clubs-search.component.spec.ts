import { ActivatedRoute, Router } from '@angular/router';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { fakeActivatedRoute, makeClub } from '@testing';

import { ClubsSearchComponent } from './clubs-search.component';
import { ClubsService } from '../../services/clubs.service';
import { ClubsApiResponse } from '../../models/club.types';
import {
  ApiClientClientError,
  ApiClientServerError,
  GENERIC_API_ERROR_MESSAGE,
} from '../../../../core/api/models/api-client-error.model';

describe('ClubsSearchComponent', () => {
  let fixture: ComponentFixture<ClubsSearchComponent>;
  let component: ClubsSearchComponent;
  let route: ReturnType<typeof fakeActivatedRoute>;
  let clubsService: jasmine.SpyObj<ClubsService>;
  let router: jasmine.SpyObj<Router>;

  function response(overrides: Partial<ClubsApiResponse> = {}): ClubsApiResponse {
    return {
      success: true,
      message: 'ok',
      data: { items: [], totalCount: 0, page: 1, pageSize: 18, totalPages: 0 },
      error: null,
      meta: { source: 'elasticsearch' },
      ...overrides,
    } as ClubsApiResponse;
  }

  beforeEach(async () => {
    route = fakeActivatedRoute();
    clubsService = jasmine.createSpyObj<ClubsService>('ClubsService', ['getClubs']);
    clubsService.getClubs.and.returnValue(of(response()));
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [ClubsSearchComponent],
      providers: [
        { provide: ActivatedRoute, useValue: route.route },
        { provide: ClubsService, useValue: clubsService },
        { provide: Router, useValue: router },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ClubsSearchComponent);
    component = fixture.componentInstance;
  });

  /** The last query params the component asked the router to write. */
  function lastQueryParams(): Record<string, string> {
    const calls = router.navigate.calls.all();
    return calls[calls.length - 1].args[1]?.queryParams as Record<string, string>;
  }

  describe('hydration from the URL', () => {
    it('starts with defaults and fetches page one', () => {
      fixture.detectChanges();

      expect(component.searchQuery).toBe('');
      expect(component.selectedClubType).toBeNull();
      expect(component.selectedSort).toBe('Relevance');
      expect(component.currentPage).toBe(1);
      expect(clubsService.getClubs).toHaveBeenCalledOnceWith({
        search: undefined,
        clubType: undefined,
        sortBy: 'Relevance',
        page: 1,
        pageSize: 18,
      });
    });

    it('reads every supported parameter', () => {
      route.setQueryParams({
        search: '  robotics  ',
        type: 'Academic',
        sort: 'Members',
        page: '3',
      });
      fixture.detectChanges();

      expect(component.searchQuery).toBe('robotics');
      expect(component.selectedClubType).toBe('Academic');
      expect(component.selectedSort).toBe('Members');
      expect(component.currentPage).toBe(3);
    });

    it('rewrites the URL instead of fetching when a parameter is not canonical', () => {
      // `type=Nonsense` and `page=0` both normalize away, so the URL is stale.
      route.setQueryParams({ type: 'Nonsense', page: '0' });
      fixture.detectChanges();

      expect(clubsService.getClubs).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalled();
      expect(lastQueryParams()).toEqual({});
    });

    it('discards an unknown sort value', () => {
      route.setQueryParams({ sort: 'Vibes' });
      fixture.detectChanges();

      expect(component.selectedSort).toBe('Relevance');
    });

    it('discards a non-numeric or negative page', () => {
      route.setQueryParams({ page: 'three' });
      fixture.detectChanges();
      expect(component.currentPage).toBe(1);

      route.setQueryParams({ page: '-2' });
      expect(component.currentPage).toBe(1);
    });
  });

  describe('URL writing', () => {
    beforeEach(() => fixture.detectChanges());

    it('omits defaults from the query string', () => {
      component.selectClubType(null);

      expect(lastQueryParams()).toEqual({});
    });

    it('writes only the non-default values', () => {
      component.searchQuery = 'robotics';
      component.selectedSort = 'Rating';
      component.currentPage = 4;
      component.selectClubType('Gaming');

      // Selecting a type resets to page one.
      expect(lastQueryParams()).toEqual({
        search: 'robotics',
        type: 'Gaming',
        sort: 'Rating',
      });
    });

    it('replaces the URL rather than pushing history', () => {
      component.onFilterChange();

      expect(router.navigate.calls.mostRecent().args[1]?.replaceUrl).toBeTrue();
    });
  });

  describe('text search', () => {
    beforeEach(() => fixture.detectChanges());

    it('debounces keystrokes into a single URL sync', fakeAsync(() => {
      router.navigate.calls.reset();

      component.searchQuery = 'r';
      component.onTextChange();
      component.searchQuery = 'ro';
      component.onTextChange();
      component.searchQuery = 'rob';
      component.onTextChange();

      tick(399);
      expect(router.navigate).not.toHaveBeenCalled();

      tick(1);
      expect(router.navigate).toHaveBeenCalledTimes(1);
      expect(lastQueryParams()).toEqual({ search: 'rob' });
    }));

    it('resets to page one as soon as the text changes', fakeAsync(() => {
      component.currentPage = 5;

      component.onTextChange();

      expect(component.currentPage).toBe(1);
      tick(400);
    }));

    it('stops syncing once the component is destroyed', fakeAsync(() => {
      component.onTextChange();
      component.ngOnDestroy();
      router.navigate.calls.reset();

      tick(400);

      expect(router.navigate).not.toHaveBeenCalled();
    }));
  });

  describe('filter chips', () => {
    beforeEach(() => fixture.detectChanges());

    it('lists only the filters that differ from the defaults', () => {
      expect(component.hasActiveFilters).toBeFalse();

      component.searchQuery = 'robotics';
      component.selectedClubType = 'Gaming';
      component.selectedSort = 'Rating';

      expect(component.activeFilters.map((c) => c.kind)).toEqual(['search', 'type', 'sort']);
      expect(component.activeFilters[2].label).toBe('Sort: Top rated');
    });

    it('clears just the chip that was dismissed', () => {
      component.searchQuery = 'robotics';
      component.selectedClubType = 'Gaming';
      component.currentPage = 3;

      component.clearChip({ kind: 'type', label: 'Gaming' });

      expect(component.selectedClubType).toBeNull();
      expect(component.searchQuery).toBe('robotics');
      expect(component.currentPage).toBe(1);
    });

    it('clears every filter at once', () => {
      component.searchQuery = 'robotics';
      component.selectedClubType = 'Gaming';
      component.selectedSort = 'Rating';
      component.currentPage = 3;

      component.clearFilters();

      expect(component.hasActiveFilters).toBeFalse();
      expect(component.currentPage).toBe(1);
      expect(lastQueryParams()).toEqual({});
    });
  });

  describe('results', () => {
    it('stores the page and the index that served it', () => {
      clubsService.getClubs.and.returnValue(
        of(
          response({
            data: {
              items: [makeClub({ id: 1 })],
              totalCount: 1,
              page: 1,
              pageSize: 18,
              totalPages: 1,
            },
          }),
        ),
      );

      fixture.detectChanges();

      expect(component.clubs.length).toBe(1);
      expect(component.totalCount).toBe(1);
      expect(component.totalPages).toBe(1);
      expect(component.sourceLabel).toBe('Search index');
      expect(component.loading).toBeFalse();
    });

    it('labels a database fallback distinctly', () => {
      clubsService.getClubs.and.returnValue(of(response({ meta: { source: 'database' } })));

      fixture.detectChanges();

      expect(component.sourceLabel).toBe('Fallback results');
    });

    it('reads the source from a legacy PascalCase Meta envelope', () => {
      clubsService.getClubs.and.returnValue(
        of({ ...response({ meta: null }), Meta: { source: 'database' } } as ClubsApiResponse),
      );

      fixture.detectChanges();

      expect(component.sourceLabel).toBe('Fallback results');
    });

    it('blanks the source when the envelope carries no metadata', () => {
      clubsService.getClubs.and.returnValue(of(response({ meta: null })));

      fixture.detectChanges();

      expect(component.resultSource).toBe('');
      expect(component.sourceLabel).toBe('');
    });

    it('reports the envelope message when a successful response carries no data', () => {
      clubsService.getClubs.and.returnValue(
        of(response({ data: null, message: 'Search is temporarily unavailable.' })),
      );

      fixture.detectChanges();

      expect(component.clubs).toEqual([]);
      expect(component.error).toBe('Search is temporarily unavailable.');
      expect(component.loading).toBeFalse();
    });

    it('shows the server message for a 4xx', () => {
      clubsService.getClubs.and.returnValue(
        throwError(() => new ApiClientClientError('That filter is not supported.', 400, 'BAD')),
      );

      fixture.detectChanges();

      expect(component.error).toBe('That filter is not supported.');
      expect(component.clubs).toEqual([]);
    });

    it('shows a generic message for a 5xx', () => {
      clubsService.getClubs.and.returnValue(
        throwError(() => new ApiClientServerError(GENERIC_API_ERROR_MESSAGE, 500)),
      );

      fixture.detectChanges();

      expect(component.error).toBe(GENERIC_API_ERROR_MESSAGE);
    });
  });

  describe('pagination', () => {
    beforeEach(() => {
      fixture.detectChanges();
      component.totalPages = 10;
      spyOn(window, 'scrollTo');
    });

    it('ignores a page outside the range', () => {
      router.navigate.calls.reset();

      component.goToPage(0);
      component.goToPage(11);

      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('navigates and scrolls to the top for a valid page', () => {
      component.goToPage(4);

      expect(component.currentPage).toBe(4);
      expect(lastQueryParams()).toEqual({ page: '4' });
      expect(window.scrollTo).toHaveBeenCalled();
    });

    it('lists every page when there are few enough', () => {
      component.totalPages = 5;

      expect(component.pageNumbers).toEqual([1, 2, 3, 4, 5]);
    });

    it('elides the middle of a long page list', () => {
      component.currentPage = 5;

      expect(component.pageNumbers).toEqual([1, -1, 4, 5, 6, -1, 10]);
    });
  });

  describe('display helpers', () => {
    beforeEach(() => fixture.detectChanges());

    it('reports member capacity as a percentage', () => {
      expect(
        component.memberCapacityPercent(makeClub({ memberCount: 25, maxMemberCount: 50 })),
      ).toBe(50);
    });

    it('clamps an over-subscribed club at 100', () => {
      expect(
        component.memberCapacityPercent(makeClub({ memberCount: 80, maxMemberCount: 50 })),
      ).toBe(100);
    });

    it('returns zero when the club has no cap', () => {
      expect(component.memberCapacityPercent(makeClub({ maxMemberCount: 0 }))).toBe(0);
    });

    it('navigates to a club detail page', () => {
      component.viewClub(7);

      expect(router.navigate).toHaveBeenCalledWith(['/clubs', 7]);
    });
  });
});
