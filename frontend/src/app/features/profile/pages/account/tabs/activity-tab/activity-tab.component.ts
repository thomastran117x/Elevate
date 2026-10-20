import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, of, switchMap, tap } from 'rxjs';

import { getApiClientMessage } from '../../../../../../core/api/models/api-client-error.model';
import { EmptyStateComponent } from '../../../../../../shared/common/empty-state/empty-state.component';
import {
  SecurityEventsPage,
  SecurityHistoryService,
} from '../../../../services/security-history.service';
import { toSecurityActivityView } from './security-activity-labels';

type LoadState = 'loading' | 'ready' | 'error';

/**
 * The signed-in user's security history: sign-ins, recovery, factor, provider, and session changes,
 * newest first, kept for up to 90 days.
 *
 * No MFA gate: reading your own history changes nothing, and someone checking whether their account
 * was used should not have to pass a second factor first.
 */
@Component({
  selector: 'app-activity-tab',
  standalone: true,
  imports: [DatePipe, RouterLink, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './activity-tab.component.html',
})
export class ActivityTabComponent {
  readonly pageSize = 10;

  readonly page = signal(1);
  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  private readonly result = signal<SecurityEventsPage | null>(null);

  readonly entries = computed(() => (this.result()?.items ?? []).map(toSecurityActivityView));
  readonly totalCount = computed(() => this.result()?.totalCount ?? 0);
  readonly totalPages = computed(() => Math.max(1, this.result()?.totalPages ?? 1));
  readonly hasPrevious = computed(() => this.page() > 1);
  readonly hasNext = computed(() => this.page() < this.totalPages());

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly history = inject(SecurityHistoryService);
  private readonly injector = inject(Injector);
  private readonly requests = new Subject<{ page: number; moveFocus: boolean }>();
  /** The page last asked for, so a retry repeats the request that failed. */
  private requestedPage = 1;

  constructor() {
    this.requests
      .pipe(
        tap(({ page }) => {
          this.requestedPage = page;
          this.state.set('loading');
          this.error.set('');
        }),
        // A newer page request replaces one still in flight, so a slow response cannot overwrite
        // the page the user moved on to.
        switchMap(({ page, moveFocus }) =>
          this.history.getSecurityEvents(page, this.pageSize).pipe(
            tap((result) => {
              // Retention can expire the last entries of a page between counting and paging. An
              // empty page past the first then means "go back", not "no activity at all".
              if (result.items.length === 0 && page > 1 && result.totalCount > 0) {
                this.requests.next({ page: Math.max(1, result.totalPages), moveFocus });
                return;
              }

              this.result.set(result);
              this.page.set(page);
              this.state.set('ready');
              if (moveFocus) {
                this.focusHeading();
              }
            }),
            catchError((err: unknown) => {
              this.error.set(getApiClientMessage(err, 'Unable to load your security activity.'));
              this.state.set('error');
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe();

    this.requests.next({ page: 1, moveFocus: false });
  }

  goToPage(page: number): void {
    if (
      page < 1 ||
      page > this.totalPages() ||
      page === this.page() ||
      this.state() === 'loading'
    ) {
      return;
    }

    this.requests.next({ page, moveFocus: true });
  }

  retry(): void {
    this.requests.next({ page: this.requestedPage, moveFocus: false });
  }

  /** Keyboard and screen reader users land back at the top of the list they just paged to. */
  private focusHeading(): void {
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }
}
