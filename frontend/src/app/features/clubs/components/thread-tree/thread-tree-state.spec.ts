import {
  applyThreadItem,
  createThreadNode,
  findThreadNode,
  insertThreadNode,
  mergeThreadNodes,
  mergeUniqueThreadNodes,
  ThreadItem,
} from './thread-tree-state';

interface TestItem extends ThreadItem {
  content: string;
}

describe('thread tree state', () => {
  it('creates nodes and finds values at arbitrary depths', () => {
    const root = createThreadNode(item(1));
    const child = createThreadNode(item(2));
    const grandchild = createThreadNode(item(3));
    root.children = [child];
    child.children = [grandchild];

    expect(findThreadNode([root], 3)).toBe(grandchild);
    expect(findThreadNode([root], 99)).toBeNull();
  });

  it('updates server fields while preserving local node state and viewer reaction', () => {
    const node = createThreadNode(item(1));
    node.replyOpen = true;
    node.replyText = 'draft';
    node.currentUserReaction = 'Like';

    applyThreadItem(node, { ...item(1), content: 'updated', currentUserReaction: null }, true);

    expect(node.content).toBe('updated');
    expect(node.replyOpen).toBeTrue();
    expect(node.replyText).toBe('draft');
    expect(node.currentUserReaction).toBe('Like');

    applyThreadItem(node, { ...item(1), currentUserReaction: null });
    expect(node.currentUserReaction).toBeNull();
  });

  it('merges duplicate pages, retains reconciled nodes, and uses ids to break timestamp ties', () => {
    const first = createThreadNode(item(1, '2026-08-15T12:00:00Z'));
    first.editOpen = true;
    const stale = createThreadNode(item(4, '2026-08-14T12:00:00Z'));
    const updatedFirst = createThreadNode({ ...item(1, first.createdAt), content: 'updated' });
    const second = createThreadNode(item(2, first.createdAt));

    const reconciled = mergeThreadNodes([first, stale], [updatedFirst, second], 'Newest', true);
    expect(reconciled.map((node) => node.id)).toEqual([2, 1, 4]);
    expect(reconciled[1]).toBe(first);
    expect(reconciled[1].content).toBe('updated');
    expect(reconciled[1].editOpen).toBeTrue();

    expect(mergeThreadNodes([], [second], 'Newest')).toEqual([second]);

    const deduplicated = mergeUniqueThreadNodes(reconciled, [updatedFirst], 'Oldest');
    expect(deduplicated.map((node) => node.id)).toEqual([4, 1, 2]);
    expect(
      insertThreadNode(deduplicated, createThreadNode(item(3, first.createdAt)), 'Newest'),
    ).toEqual([
      jasmine.objectContaining({ id: 3 }),
      jasmine.objectContaining({ id: 2 }),
      first,
      stale,
    ]);
  });
});

function item(
  id: number,
  createdAt = `2026-08-${String(id).padStart(2, '0')}T12:00:00Z`,
): TestItem {
  return { id, createdAt, currentUserReaction: null, content: `item ${id}` };
}
