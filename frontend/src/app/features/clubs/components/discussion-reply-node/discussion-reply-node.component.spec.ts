import { TestBed } from '@angular/core/testing';

import { DiscussionReplyNode } from '../discussion-reply-thread/discussion-reply-thread.component';
import { DiscussionReplyNodeComponent } from './discussion-reply-node.component';

describe('DiscussionReplyNodeComponent', () => {
  it('renders arbitrary nested depth while capping indentation', async () => {
    await TestBed.configureTestingModule({
      imports: [DiscussionReplyNodeComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(DiscussionReplyNodeComponent);
    fixture.componentRef.setInput('node', nestedNode(7));
    fixture.detectChanges();

    const articles = fixture.nativeElement.querySelectorAll('article') as NodeListOf<HTMLElement>;
    expect(articles.length).toBe(7);
    expect(articles[4].style.marginLeft).toBe('0px');
    expect(articles[6].style.marginLeft).toBe('0px');
  });

  it('shows a placeholder without author content for a soft-deleted reply', async () => {
    await TestBed.configureTestingModule({
      imports: [DiscussionReplyNodeComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(DiscussionReplyNodeComponent);
    fixture.componentRef.setInput('node', { ...nestedNode(1), isDeleted: true, content: null });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Reply deleted');
    expect(fixture.nativeElement.textContent).not.toContain('Reply 1');
  });
});

function nestedNode(depth: number, id = 1): DiscussionReplyNode {
  const child = depth > 1 ? nestedNode(depth - 1, id + 1) : null;
  return {
    id,
    discussionId: 9,
    parentReplyId: id === 1 ? null : id - 1,
    userId: 7,
    content: `Reply ${id}`,
    author: { id: 7, name: 'Taylor', username: 'taylor', avatar: null },
    isDeleted: false,
    createdAt: '2026-08-14T00:00:00Z',
    updatedAt: '2026-08-14T00:00:00Z',
    likeCount: 0,
    dislikeCount: 0,
    currentUserReaction: null,
    directReplyCount: child ? 1 : 0,
    children: child ? [child] : [],
    childrenLoaded: !!child,
    loadingChildren: false,
    nextCursor: null,
    hasMoreChildren: false,
    replyOpen: false,
    replyText: '',
    editOpen: false,
    editText: '',
    deleteConfirm: false,
    busy: false,
    error: '',
  };
}
