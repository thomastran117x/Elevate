import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';

import { ActivityTabComponent } from './activity-tab.component';
import {
  AccountSecurityEvent,
  SecurityEventsPage,
  SecurityHistoryService,
} from '../../../../services/security-history.service';
import { ApiClientClientError } from '../../../../../../core/api/models/api-client-error.model';

describe('ActivityTabComponent', () => {
  let history: jasmine.SpyObj<Pick<SecurityHistoryService, 'getSecurityEvents'>>;

  function makeEntry(
    id: number,
    overrides: Partial<AccountSecurityEvent> = {},
  ): AccountSecurityEvent {
    return {
      id,
      kind: 'signed_in',
      occurredAt: '2026-10-20T12:00:00Z',
      method: 'password',
      factor: null,
      reason: null,
      client: 'chrome',
      deviceClass: 'desktop',
      network: '203.0.113.0/24',
      initiatedByAdmin: false,
      reference: `req-${id}`,
      ...overrides,
    };
  }

  function makePage(
    page: number,
    items: AccountSecurityEvent[],
    totalCount = items.length,
  ): SecurityEventsPage {
    return { items, totalCount, page, pageSize: 10, totalPages: Math.ceil(totalCount / 10) };
  }

  function createFixture(): ComponentFixture<ActivityTabComponent> {
    TestBed.configureTestingModule({
      imports: [ActivityTabComponent],
      providers: [provideRouter([]), { provide: SecurityHistoryService, useValue: history }],
    });

    const fixture = TestBed.createComponent(ActivityTabComponent);
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<ActivityTabComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function button(
    fixture: ComponentFixture<ActivityTabComponent>,
    label: string,
  ): HTMLButtonElement {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((candidate) => candidate.textContent?.trim() === label)!;
  }

  beforeEach(() => {
    history = jasmine.createSpyObj('SecurityHistoryService', ['getSecurityEvents']);
  });

  afterEach(() => TestBed.resetTestingModule());

  it('announces that it is loading until the first page arrives', () => {
    const pending = new Subject<SecurityEventsPage>();
    history.getSecurityEvents.and.returnValue(pending);

    const fixture = createFixture();

    const status = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(status?.textContent).toContain('Loading your security activity');
    expect(history.getSecurityEvents).toHaveBeenCalledWith(1, 10);

    pending.next(makePage(1, [makeEntry(1)]));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="status"]')).toBeNull();
  });

  it('lists entries newest first with their context and support reference', () => {
    history.getSecurityEvents.and.returnValue(
      of(
        makePage(1, [
          makeEntry(2, { kind: 'password_changed', method: null, initiatedByAdmin: true }),
          makeEntry(1),
        ]),
      ),
    );

    const fixture = createFixture();

    const items = (fixture.nativeElement as HTMLElement).querySelectorAll('ol li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Password changed');
    expect(items[0].textContent).toContain('Important');
    expect(items[0].textContent).toContain('Done by an administrator.');
    expect(items[1].textContent).toContain('Signed in with your password.');
    expect(items[1].textContent).toContain('Chrome · Desktop · 203.0.113.0/24');
    expect(items[1].textContent).toContain('Support reference: req-1');
    expect(items[1].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-20T12:00:00Z');
    expect(text(fixture)).toContain('Page 1 of 1');
    expect(text(fixture)).toContain('2 entries');
  });

  it('shows an empty state when there is no activity', () => {
    history.getSecurityEvents.and.returnValue(of(makePage(1, [], 0)));

    const fixture = createFixture();

    expect(text(fixture)).toContain('No recent security activity');
    expect((fixture.nativeElement as HTMLElement).querySelector('ol')).toBeNull();
  });

  it('reports a failure and retries the same page on request', () => {
    history.getSecurityEvents.and.returnValues(
      throwError(() => new ApiClientClientError('Session expired.', 401, 'UNAUTHORIZED')),
      of(makePage(1, [makeEntry(1)])),
    );

    const fixture = createFixture();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Session expired.');

    button(fixture, 'Try again').click();
    fixture.detectChanges();

    expect(history.getSecurityEvents.calls.mostRecent().args).toEqual([1, 10]);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeNull();
    expect(text(fixture)).toContain('Signed in');
  });

  it('pages forward and back, and keeps the edges disabled', async () => {
    const firstPage = makePage(1, [makeEntry(12)], 12);
    const secondPage = makePage(2, [makeEntry(2)], 12);
    history.getSecurityEvents.and.callFake((page?: number): Observable<SecurityEventsPage> =>
      of(page === 2 ? secondPage : firstPage),
    );

    const fixture = createFixture();
    expect(button(fixture, 'Previous').disabled).toBeTrue();
    expect(button(fixture, 'Next').disabled).toBeFalse();

    button(fixture, 'Next').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(history.getSecurityEvents).toHaveBeenCalledWith(2, 10);
    expect(text(fixture)).toContain('Page 2 of 2');
    expect(button(fixture, 'Next').disabled).toBeTrue();
    // Focus returns to the top of the list for keyboard and screen reader users.
    expect(document.activeElement?.id).toBe('security-activity-heading');

    button(fixture, 'Previous').click();
    fixture.detectChanges();

    expect(history.getSecurityEvents.calls.mostRecent().args).toEqual([1, 10]);
    expect(text(fixture)).toContain('Page 1 of 2');
  });

  it('stops at the furthest page the API accepts', () => {
    // 6,000 entries at ten a page would be 600 pages, but the API refuses anything past 500.
    history.getSecurityEvents.and.callFake((page?: number): Observable<SecurityEventsPage> =>
      of({ ...makePage(page ?? 1, [makeEntry(page ?? 1)], 6000), page: page ?? 1 }),
    );
    const fixture = createFixture();

    expect(text(fixture)).toContain('Page 1 of 500');

    fixture.componentInstance.goToPage(500);
    fixture.detectChanges();

    expect(text(fixture)).toContain('Page 500 of 500');
    expect(button(fixture, 'Next').disabled).toBeTrue();
    fixture.componentInstance.goToPage(501);
    expect(history.getSecurityEvents).not.toHaveBeenCalledWith(501, 10);
  });

  it('ignores page requests outside the range or for the current page', () => {
    history.getSecurityEvents.and.returnValue(of(makePage(1, [makeEntry(1)], 12)));
    const fixture = createFixture();
    history.getSecurityEvents.calls.reset();

    fixture.componentInstance.goToPage(0);
    fixture.componentInstance.goToPage(1);
    fixture.componentInstance.goToPage(3);

    expect(history.getSecurityEvents).not.toHaveBeenCalled();
  });

  it('falls back to the last page when retention empties the requested one', () => {
    // Eleven entries at first; by the time page 2 is asked for, its only entry has expired.
    history.getSecurityEvents.and.returnValues(
      of(makePage(1, [makeEntry(11)], 11)),
      of(makePage(2, [], 10)),
      of(makePage(1, [makeEntry(11)], 10)),
    );

    const fixture = createFixture();
    button(fixture, 'Next').click();
    fixture.detectChanges();

    expect(history.getSecurityEvents.calls.allArgs()).toEqual([
      [1, 10],
      [2, 10],
      [1, 10],
    ]);
    expect(text(fixture)).not.toContain('No recent security activity');
    expect(text(fixture)).toContain('Page 1 of 1');
  });

  it('does not loop when every entry on a page is unreadable', () => {
    // Page 3 of 3 exists, but the normalizer dropped all of its entries (a renamed field, say).
    history.getSecurityEvents.and.callFake((page?: number): Observable<SecurityEventsPage> =>
      of(page === 3 ? makePage(3, [], 25) : makePage(page ?? 1, [makeEntry(page ?? 1)], 25)),
    );

    const fixture = createFixture();
    button(fixture, 'Next').click();
    fixture.detectChanges();
    button(fixture, 'Next').click();
    fixture.detectChanges();

    expect(history.getSecurityEvents.calls.allArgs()).toEqual([
      [1, 10],
      [2, 10],
      [3, 10],
    ]);
    // There is activity, so the pager stays and the user can page back.
    expect(text(fixture)).not.toContain('No recent security activity');
    expect(text(fixture)).toContain('Page 3 of 3');
    expect(button(fixture, 'Previous').disabled).toBeFalse();
  });

  it('ignores page requests while a page is loading', () => {
    const pending = new Subject<SecurityEventsPage>();
    history.getSecurityEvents.and.returnValue(pending);
    const fixture = createFixture();

    fixture.componentInstance.goToPage(2);

    expect(history.getSecurityEvents).toHaveBeenCalledTimes(1);
  });
});
