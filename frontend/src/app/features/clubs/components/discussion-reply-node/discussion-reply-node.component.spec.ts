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

  it('derives ownership, author fallbacks, and initials', () => {
    const component = new DiscussionReplyNodeComponent();
    component.node = nestedNode(1);

    expect(component.isOwnReply).toBeFalse();
    component.currentUser = { Id: 7 } as never;
    expect(component.isOwnReply).toBeTrue();
    expect(component.authorName).toBe('Taylor');
    expect(component.initials).toBe('T');

    component.node.author = { id: 7, name: null, username: 'taylor', avatar: null };
    expect(component.authorName).toBe('taylor');
    component.node.author = null;
    expect(component.authorName).toBe('User #7');
    component.node.isDeleted = true;
    expect(component.authorName).toBe('Deleted reply');
  });

  it('formats recent and older reply dates', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date('2026-08-15T12:00:00Z'));
    const component = new DiscussionReplyNodeComponent();

    expect(component.formatDate('2026-08-15T11:59:30Z')).toBe('just now');
    expect(component.formatDate('2026-08-15T11:45:00Z')).toBe('15m ago');
    expect(component.formatDate('2026-08-15T09:00:00Z')).toBe('3h ago');
    expect(component.formatDate('2026-08-13T12:00:00Z')).toBe('2d ago');
    expect(component.formatDate('2026-08-01T12:00:00Z')).toContain('Aug');
    jasmine.clock().uninstall();
  });

  it('opens controls and emits only valid create, edit, and reaction actions', () => {
    const component = new DiscussionReplyNodeComponent();
    component.node = nestedNode(1);
    const actions: unknown[] = [];
    component.action.subscribe((action) => actions.push(action));

    component.node.error = 'old error';
    component.toggleReply();
    expect(component.node.replyOpen).toBeTrue();
    expect(component.node.error).toBe('');

    component.node.replyText = '   ';
    component.submitChild();
    component.node.replyText = '  Child reply  ';
    component.submitChild();

    component.node.content = 'Existing';
    component.startEdit();
    expect(component.node.editText).toBe('Existing');
    component.node.editText = '   ';
    component.saveEdit();
    component.node.editText = '  Edited reply  ';
    component.saveEdit();
    component.react('Dislike');

    expect(actions).toEqual([
      { type: 'create', node: component.node, content: 'Child reply' },
      { type: 'edit', node: component.node, content: 'Edited reply' },
      { type: 'react', node: component.node, reaction: 'Dislike' },
    ]);
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
