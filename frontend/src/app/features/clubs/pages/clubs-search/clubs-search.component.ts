import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterModule } from '@angular/router';
import { Subject, debounceTime, takeUntil } from 'rxjs';

import { ClubsService } from '../../services/clubs.service';
import {
  ALL_CLUB_SORTS,
  ALL_CLUB_TYPES,
  CLUB_TYPE_STYLES,
  Club,
  ClubSortBy,
  ClubType,
} from '../../models/club.types';
import { extractEnvelopeData } from '../../../../core/api/models/api-envelope.model';

type SearchPageState = {
  searchQuery: string;
  selectedClubType: ClubType | null;
  selectedSort: ClubSortBy;
  currentPage: number;
};

type FilterChip = {
  kind: 'search' | 'type' | 'sort';
  value?: string;
  label: string;
};

@Component({
  selector: 'app-clubs-search',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './clubs-search.component.html',
})
export class ClubsSearchComponent implements OnInit, OnDestroy {
  private static readonly DEFAULT_SORT: ClubSortBy = 'Relevance';

  searchQuery = '';
  selectedClubType: ClubType | null = null;
  selectedSort: ClubSortBy = ClubsSearchComponent.DEFAULT_SORT;
  currentPage = 1;
  readonly pageSize = 18;

  clubs: Club[] = [];
  totalCount = 0;
  totalPages = 0;
  loading = false;
  error = '';
  resultSource = '';

  readonly allClubTypes = ALL_CLUB_TYPES;
  readonly clubTypeStyles = CLUB_TYPE_STYLES;
  readonly sortOptions: Array<{ value: ClubSortBy; label: string }> = [
    { value: 'Relevance', label: 'Best match' },
    { value: 'Newest', label: 'Newest first' },
    { value: 'Members', label: 'Most members' },
    { value: 'Rating', label: 'Top rated' },
  ];

  private readonly textChange$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();
  private requestVersion = 0;

  constructor(
    private clubsService: ClubsService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.textChange$
      .pipe(debounceTime(400), takeUntil(this.destroy$))
      .subscribe(() => this.syncUrlToState());

    this.route.queryParamMap
      .pipe(takeUntil(this.destroy$))
      .subscribe((params) => this.applyRouteState(params));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get hasActiveFilters(): boolean {
    return this.activeFilters.length > 0;
  }

  get pageNumbers(): number[] {
    const total = this.totalPages;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

    const pages: number[] = [1];
    const start = Math.max(2, this.currentPage - 1);
    const end = Math.min(total - 1, this.currentPage + 1);

    if (start > 2) pages.push(-1);
    for (let index = start; index <= end; index++) pages.push(index);
    if (end < total - 1) pages.push(-1);
    pages.push(total);

    return pages;
  }

  get activeFilters(): FilterChip[] {
    const chips: FilterChip[] = [];
    if (this.searchQuery) chips.push({ kind: 'search', label: `Search: ${this.searchQuery}` });
    if (this.selectedClubType) chips.push({ kind: 'type', label: this.selectedClubType });
    if (this.selectedSort !== ClubsSearchComponent.DEFAULT_SORT) {
      chips.push({
        kind: 'sort',
        label: `Sort: ${this.sortOptions.find((o) => o.value === this.selectedSort)?.label ?? this.selectedSort}`,
      });
    }
    return chips;
  }

  get sourceLabel(): string {
    if (this.resultSource === 'database') return 'Fallback results';
    if (this.resultSource === 'elasticsearch') return 'Search index';
    return this.resultSource;
  }

  onTextChange(): void {
    this.currentPage = 1;
    this.textChange$.next();
  }

  selectClubType(type: ClubType | null): void {
    this.selectedClubType = type;
    this.currentPage = 1;
    this.syncUrlToState();
  }

  onFilterChange(): void {
    this.currentPage = 1;
    this.syncUrlToState();
  }

  clearChip(chip: FilterChip): void {
    switch (chip.kind) {
      case 'search':
        this.searchQuery = '';
        break;
      case 'type':
        this.selectedClubType = null;
        break;
      case 'sort':
        this.selectedSort = ClubsSearchComponent.DEFAULT_SORT;
        break;
    }
    this.currentPage = 1;
    this.syncUrlToState();
  }

  clearFilters(): void {
    this.searchQuery = '';
    this.selectedClubType = null;
    this.selectedSort = ClubsSearchComponent.DEFAULT_SORT;
    this.currentPage = 1;
    this.syncUrlToState();
  }

  viewClub(clubId: number): void {
    this.router.navigate(['/clubs', clubId]);
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.currentPage = page;
    this.syncUrlToState();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  memberCapacityPercent(club: Club): number {
    if (club.maxMemberCount <= 0) return 0;
    return Math.min(100, (club.memberCount / club.maxMemberCount) * 100);
  }

  private applyRouteState(params: ParamMap): void {
    const nextState = this.readStateFromParams(params);
    const needsUrlSync = this.stateRequiresCanonicalUrl(params, nextState);

    this.searchQuery = nextState.searchQuery;
    this.selectedClubType = nextState.selectedClubType;
    this.selectedSort = nextState.selectedSort;
    this.currentPage = nextState.currentPage;

    if (needsUrlSync) {
      this.syncUrlToState();
      return;
    }

    this.fetch();
  }

  private fetch(): void {
    const requestVersion = ++this.requestVersion;
    this.loading = true;
    this.error = '';

    this.clubsService
      .getClubs({
        search: this.searchQuery || undefined,
        clubType: this.selectedClubType ?? undefined,
        sortBy: this.selectedSort,
        page: this.currentPage,
        pageSize: this.pageSize,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          if (requestVersion !== this.requestVersion) return;

          const data = extractEnvelopeData(response);
          if (!data) {
            this.resetResults();
            this.error =
              (response as { message?: string; Message?: string }).message ||
              (response as { message?: string; Message?: string }).Message ||
              'Failed to load clubs. Please try again.';
            this.loading = false;
            return;
          }

          this.clubs = data.items;
          this.totalCount = data.totalCount;
          this.totalPages = data.totalPages;
          this.resultSource =
            typeof (response as { meta?: { source?: string } }).meta?.source === 'string'
              ? ((response as { meta?: { source?: string } }).meta?.source ?? '')
              : '';
          this.loading = false;
        },
        error: (response) => {
          if (requestVersion !== this.requestVersion) return;
          this.resetResults();
          this.error =
            response?.error?.message ||
            response?.error?.Message ||
            'Failed to load clubs. Please try again.';
          this.loading = false;
        },
      });
  }

  private resetResults(): void {
    this.clubs = [];
    this.totalCount = 0;
    this.totalPages = 0;
    this.resultSource = '';
  }

  private readStateFromParams(params: ParamMap): SearchPageState {
    return {
      searchQuery: (params.get('search') ?? '').trim(),
      selectedClubType: this.parseClubType(params.get('type')),
      selectedSort: this.parseSort(params.get('sort')),
      currentPage: this.parsePage(params.get('page')),
    };
  }

  private stateRequiresCanonicalUrl(params: ParamMap, state: SearchPageState): boolean {
    const current = this.serializeQueryParams(
      params.keys.reduce<Record<string, string>>((acc, key) => {
        const value = params.get(key);
        if (value !== null) acc[key] = value;
        return acc;
      }, {}),
    );
    return current !== this.serializeQueryParams(this.buildQueryParams(state));
  }

  private syncUrlToState(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: this.buildQueryParams(),
      replaceUrl: true,
    });
  }

  private buildQueryParams(state: SearchPageState = this.currentState()): Record<string, string> {
    const queryParams: Record<string, string> = {};
    if (state.searchQuery) queryParams['search'] = state.searchQuery;
    if (state.selectedClubType) queryParams['type'] = state.selectedClubType;
    if (state.selectedSort !== ClubsSearchComponent.DEFAULT_SORT)
      queryParams['sort'] = state.selectedSort;
    if (state.currentPage > 1) queryParams['page'] = String(state.currentPage);
    return queryParams;
  }

  private currentState(): SearchPageState {
    return {
      searchQuery: this.searchQuery.trim(),
      selectedClubType: this.selectedClubType,
      selectedSort: this.selectedSort,
      currentPage: this.currentPage > 0 ? this.currentPage : 1,
    };
  }

  private serializeQueryParams(params: Record<string, string>): string {
    return Object.entries(params)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([k, v]) => `${k}=${v}`)
      .join('&');
  }

  private parseClubType(value: string | null): ClubType | null {
    return value && ALL_CLUB_TYPES.includes(value as ClubType) ? (value as ClubType) : null;
  }

  private parseSort(value: string | null): ClubSortBy {
    return value && ALL_CLUB_SORTS.includes(value as ClubSortBy)
      ? (value as ClubSortBy)
      : ClubsSearchComponent.DEFAULT_SORT;
  }

  private parsePage(value: string | null): number {
    if (!value) return 1;
    const page = Number.parseInt(value, 10);
    return Number.isFinite(page) && page > 0 ? page : 1;
  }
}
