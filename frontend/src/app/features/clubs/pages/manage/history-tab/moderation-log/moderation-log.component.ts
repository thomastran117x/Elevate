import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, map, of, switchMap, tap } from 'rxjs';

import { getApiClientMessage } from '../../../../../../core/api/models/api-client-error.model';
import { ClubModerationLogItem } from '../../../../models/club-moderation.types';
import { AuthorInfo } from '../../../../models/club-post.types';
import { ClubModerationService } from '../../../../services/club-moderation.service';

const ACTION_LABELS: Record<ClubModerationLogItem['action'], string> = {
  remove: 'Removed',
  edit: 'Edited',
  pin: 'Pinned',
  unpin: 'Unpinned',
};

/**
 * The club's moderation log: every time staff or an admin changed or removed content someone
 * else wrote, with the reason they gave. Readable by the club's managers, owner and admins.
 */
@Component({
  selector: 'app-moderation-log',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './moderation-log.component.html',
})
export class ModerationLogComponent implements OnInit {
  readonly clubId = input.required<number>();

  readonly pageSize = 10;
  readonly items = signal<ClubModerationLogItem[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));

  private readonly moderation = inject(ClubModerationService);
  private readonly destroyRef = inject(DestroyRef);

  /** Each requested page; a newer request cancels the one in flight, so pages never arrive out of order. */
  private readonly pageRequests = new Subject<number>();

  ngOnInit(): void {
    this.pageRequests
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set('');
        }),
        switchMap((page) =>
          this.moderation.getModerationLog(this.clubId(), page, this.pageSize).pipe(
            map((response) => ({ response, failure: null as unknown })),
            catchError((failure: unknown) => of({ response: null, failure })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ response, failure }) => {
        this.loading.set(false);
        if (failure) {
          this.error.set(getApiClientMessage(failure, 'Unable to load the moderation log.'));
          return;
        }
        this.items.set(response?.data?.items ?? []);
        this.totalCount.set(response?.data?.totalCount ?? 0);
      });

    this.pageRequests.next(this.page());
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) return;
    this.page.set(page);
    this.pageRequests.next(page);
  }

  summary(item: ClubModerationLogItem): string {
    return `${ACTION_LABELS[item.action]} ${item.contentType.toLowerCase() || 'content'}`;
  }

  partyName(party: AuthorInfo): string {
    return party.name || party.usernameDisplay || party.username || `User #${party.id}`;
  }
}
