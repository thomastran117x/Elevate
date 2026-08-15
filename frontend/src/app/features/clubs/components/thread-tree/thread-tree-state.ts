export type ThreadSort = 'Newest' | 'Oldest';

export interface ThreadItem {
  id: number;
  createdAt: string;
  currentUserReaction: unknown;
}

export type ThreadNode<TItem extends ThreadItem> = TItem & {
  children: ThreadNode<TItem>[];
  childrenLoaded: boolean;
  loadingChildren: boolean;
  nextCursor: string | null;
  hasMoreChildren: boolean;
  replyOpen: boolean;
  replyText: string;
  editOpen: boolean;
  editText: string;
  deleteConfirm: boolean;
  busy: boolean;
  error: string;
};

export function createThreadNode<TItem extends ThreadItem>(item: TItem): ThreadNode<TItem> {
  return {
    ...item,
    children: [],
    childrenLoaded: false,
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

export function findThreadNode<TItem extends ThreadItem>(
  nodes: ThreadNode<TItem>[],
  id: number,
): ThreadNode<TItem> | null {
  for (const node of nodes) {
    if (node.id === id) return node;
    const found = findThreadNode(node.children, id);
    if (found) return found;
  }
  return null;
}

export function applyThreadItem<TItem extends ThreadItem>(
  node: ThreadNode<TItem>,
  item: TItem,
  preserveViewerReaction = false,
): void {
  const currentUserReaction = node.currentUserReaction;
  const state = getNodeState(node);
  Object.assign(node, item, state);
  if (preserveViewerReaction) node.currentUserReaction = currentUserReaction;
}

export function mergeThreadNodes<TItem extends ThreadItem>(
  existing: ThreadNode<TItem>[],
  incoming: ThreadNode<TItem>[],
  sort: ThreadSort,
  preserveExisting = false,
): ThreadNode<TItem>[] {
  const byId = new Map(existing.map((node) => [node.id, node]));
  const merged = incoming.map((node) => {
    const prior = byId.get(node.id);
    if (!prior) return node;
    return Object.assign(prior, node, getNodeState(prior));
  });
  if (!preserveExisting) return merged;
  const incomingIds = new Set(incoming.map((node) => node.id));
  return [...merged, ...existing.filter((node) => !incomingIds.has(node.id))].sort((left, right) =>
    compareThreadNodes(left, right, sort),
  );
}

export function mergeUniqueThreadNodes<TItem extends ThreadItem>(
  current: ThreadNode<TItem>[],
  incoming: ThreadNode<TItem>[],
  sort: ThreadSort,
): ThreadNode<TItem>[] {
  const byId = new Map(current.map((node) => [node.id, node]));
  for (const node of incoming) {
    if (!byId.has(node.id)) byId.set(node.id, node);
  }
  return [...byId.values()].sort((left, right) => compareThreadNodes(left, right, sort));
}

export function insertThreadNode<TItem extends ThreadItem>(
  current: ThreadNode<TItem>[],
  node: ThreadNode<TItem>,
  sort: ThreadSort,
): ThreadNode<TItem>[] {
  return mergeUniqueThreadNodes(current, [node], sort);
}

function compareThreadNodes<TItem extends ThreadItem>(
  left: ThreadNode<TItem>,
  right: ThreadNode<TItem>,
  sort: ThreadSort,
): number {
  const createdAtDifference =
    new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime();
  const ascending = createdAtDifference || left.id - right.id;
  return sort === 'Newest' ? -ascending : ascending;
}

function getNodeState<TItem extends ThreadItem>(node: ThreadNode<TItem>) {
  return {
    children: node.children,
    childrenLoaded: node.childrenLoaded,
    loadingChildren: node.loadingChildren,
    nextCursor: node.nextCursor,
    hasMoreChildren: node.hasMoreChildren,
    replyOpen: node.replyOpen,
    replyText: node.replyText,
    editOpen: node.editOpen,
    editText: node.editText,
    deleteConfirm: node.deleteConfirm,
    busy: node.busy,
    error: node.error,
  };
}
