import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { envelope } from '@testing';

import { ClubModerationLogItem } from '../../../../models/club-moderation.types';
import { ClubModerationService } from '../../../../services/club-moderation.service';
import { ModerationLogComponent } from './moderation-log.component';

function makeItem(overrides: Partial<ClubModerationLogItem> = {}): ClubModerationLogItem {
  return {
    id: 1,
    clubId: 3,
    action: 'remove',
    contentType: 'Comment',
    contentId: 30,
    contentSnapshot: 'Buy cheap watches',
    reason: 'Spam',
    actorClubRole: 'Volunteer',
    actor: { id: 7, name: 'Noah Patel', username: 'noah', usernameDisplay: 'noah', avatar: null },
    author: { id: 8, name: null, username: 'ava', usernameDisplay: 'Ava', avatar: null },
    createdAt: '2026-11-13T10:00:00Z',
    ...overrides,
  };
}

function page(items: ClubModerationLogItem[], totalCount = items.length) {
  return of(envelope({ items, totalCount, page: 1, pageSize: 10, totalPages: 1 }));
}

describe('ModerationLogComponent', () => {
  let fixture: ComponentFixture<ModerationLogComponent>;
  let component: ModerationLogComponent;
  let service: jasmine.SpyObj<ClubModerationService>;

  beforeEach(async () => {
    service = jasmine.createSpyObj<ClubModerationService>('ClubModerationService', [
      'getModerationLog',
    ]);

    await TestBed.configureTestingModule({
      imports: [ModerationLogComponent],
      providers: [{ provide: ClubModerationService, useValue: service }],
    }).compileComponents();

    fixture = TestBed.createComponent(ModerationLogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('clubId', 3);
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('lists each action with its author, snapshot, reason and actor', () => {
    service.getModerationLog.and.returnValue(page([makeItem()]));

    fixture.detectChanges();

    expect(service.getModerationLog).toHaveBeenCalledWith(3, 1, 10);
    expect(text()).toContain('Removed comment');
    expect(text()).toContain('by Ava');
    expect(text()).toContain('Buy cheap watches');
    expect(text()).toContain('Reason: Spam');
    expect(text()).toContain('Noah Patel');
    expect(text()).toContain('Volunteer');
  });

  it('says so when nothing has been moderated', () => {
    service.getModerationLog.and.returnValue(page([]));

    fixture.detectChanges();

    expect(text()).toContain('No moderation actions yet.');
  });

  it('shows the server message when the log cannot be loaded', () => {
    service.getModerationLog.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 403,
            error: { success: false, message: 'Not yours to see', error: { code: 'FORBIDDEN' } },
          }),
      ),
    );

    fixture.detectChanges();

    expect(component.error()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });

  it('pages through the log and ignores pages out of range', () => {
    service.getModerationLog.and.returnValue(page([makeItem()], 25));
    fixture.detectChanges();
    expect(component.totalPages()).toBe(3);

    component.goToPage(2);
    expect(service.getModerationLog).toHaveBeenCalledWith(3, 2, 10);

    service.getModerationLog.calls.reset();
    component.goToPage(0);
    component.goToPage(4);
    component.goToPage(2);
    expect(service.getModerationLog).not.toHaveBeenCalled();
  });

  it('labels every action and falls back to the user id for an unknown user', () => {
    const unknown = { id: 12, name: null, username: null, usernameDisplay: null, avatar: null };

    expect(component.summary(makeItem({ action: 'pin', contentType: 'Post' }))).toBe('Pinned post');
    expect(component.summary(makeItem({ action: 'unpin', contentType: 'Post' }))).toBe(
      'Unpinned post',
    );
    expect(component.summary(makeItem({ action: 'edit', contentType: '' }))).toBe('Edited content');
    expect(component.partyName(unknown)).toBe('User #12');
  });
});
