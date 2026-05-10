import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';

import { EventsService } from '../../services/events.service';
import {
  EventItem,
  EventCategory,
  EventStatus,
  EventSortBy,
  ALL_CATEGORIES,
  CATEGORY_STYLES,
} from '../../models/event.types';

@Component({
  selector: 'app-events-search',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './events-search.component.html',
})
export class EventsSearchComponent implements OnInit, OnDestroy {
  // Filter state
  searchQuery = '';
  cityQuery = '';
  selectedCategory: EventCategory | null = null;
  selectedStatus: EventStatus | null = null;
  selectedSort: EventSortBy = 'Relevance';

  // Pagination
  currentPage = 1;
  readonly pageSize = 20;

  // Results
  events: EventItem[] = [];
  totalCount = 0;
  totalPages = 0;
  loading = false;
  error = '';

  // Constants exposed to template
  readonly allCategories = ALL_CATEGORIES;
  readonly categoryStyles = CATEGORY_STYLES;
  readonly sortOptions: { value: EventSortBy; label: string }[] = [
    { value: 'Relevance', label: 'Relevance' },
    { value: 'Date', label: 'Date' },
    { value: 'Popularity', label: 'Popularity' },
  ];
  readonly statusOptions: { value: EventStatus | null; label: string }[] = [
    { value: null, label: 'All events' },
    { value: 'Upcoming', label: 'Upcoming' },
    { value: 'Ongoing', label: 'Ongoing' },
    { value: 'Closed', label: 'Closed' },
  ];

  private readonly filterChange$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private eventsService: EventsService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    // Seed state from URL query params on first load
    const params = this.route.snapshot.queryParams;
    this.searchQuery = params['search'] ?? '';
    this.cityQuery = params['city'] ?? '';
    this.selectedCategory = params['category'] ?? null;
    this.selectedStatus = params['status'] ?? null;
    this.selectedSort = params['sort'] ?? 'Relevance';
    this.currentPage = parseInt(params['page'] ?? '1', 10) || 1;

    // Debounce filter changes so text typing doesn't hammer the API
    this.filterChange$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => this.fetch());

    // Initial fetch
    this.fetch();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onTextChange(): void {
    this.currentPage = 1;
    this.filterChange$.next();
  }

  selectCategory(cat: EventCategory | null): void {
    this.selectedCategory = cat;
    this.currentPage = 1;
    this.fetch();
  }

  onFilterChange(): void {
    this.currentPage = 1;
    this.fetch();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.currentPage = page;
    this.fetch();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  get pageNumbers(): number[] {
    const total = this.totalPages;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

    const pages: number[] = [1];
    const start = Math.max(2, this.currentPage - 1);
    const end = Math.min(total - 1, this.currentPage + 1);

    if (start > 2) pages.push(-1); // ellipsis
    for (let i = start; i <= end; i++) pages.push(i);
    if (end < total - 1) pages.push(-1); // ellipsis
    pages.push(total);
    return pages;
  }

  formatDate(iso: string): string {
    const d = new Date(iso);
    return d.toLocaleDateString('en-CA', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString('en-CA', {
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  formatCost(cost: number): string {
    return cost === 0 ? 'Free' : `$${cost}`;
  }

  private fetch(): void {
    this.updateUrlParams();
    this.loading = true;
    this.error = '';

    this.eventsService
      .getEvents({
        search: this.searchQuery || undefined,
        city: this.cityQuery || undefined,
        category: this.selectedCategory ?? undefined,
        status: this.selectedStatus ?? undefined,
        sortBy: this.selectedSort,
        page: this.currentPage,
        pageSize: this.pageSize,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          if (!res.data) {
            this.error = res.message || 'Failed to load events. Please try again.';
            this.events = [];
            this.totalCount = 0;
            this.totalPages = 0;
            this.loading = false;
            return;
          }

          this.events = res.data.items;
          this.totalCount = res.data.totalCount;
          this.totalPages = res.data.totalPages;
          this.loading = false;
        },
        error: () => {
          this.error = 'Failed to load events. Please try again.';
          this.loading = false;
        },
      });
  }

  private updateUrlParams(): void {
    const queryParams: Record<string, string | undefined> = {};
    if (this.searchQuery) queryParams['search'] = this.searchQuery;
    if (this.cityQuery) queryParams['city'] = this.cityQuery;
    if (this.selectedCategory) queryParams['category'] = this.selectedCategory;
    if (this.selectedStatus) queryParams['status'] = this.selectedStatus;
    if (this.selectedSort !== 'Relevance') queryParams['sort'] = this.selectedSort;
    if (this.currentPage > 1) queryParams['page'] = String(this.currentPage);

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      replaceUrl: true,
    });
  }
}
