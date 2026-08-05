export type StorageKind = 'local' | 'session';

function storageProperty(kind: StorageKind): 'localStorage' | 'sessionStorage' {
  return kind === 'local' ? 'localStorage' : 'sessionStorage';
}

function install(kind: StorageKind, storage: Storage): () => void {
  const property = storageProperty(kind);
  const original = Object.getOwnPropertyDescriptor(window, property);

  Object.defineProperty(window, property, { value: storage, configurable: true });

  return () => {
    if (original) {
      Object.defineProperty(window, property, original);
    } else {
      delete (window as unknown as Record<string, unknown>)[property];
    }
  };
}

function memoryStorage(seed: Record<string, string> = {}): Storage {
  const map = new Map<string, string>(Object.entries(seed));

  return {
    get length() {
      return map.size;
    },
    clear: () => map.clear(),
    getItem: (key: string) => map.get(key) ?? null,
    key: (index: number) => Array.from(map.keys())[index] ?? null,
    removeItem: (key: string) => void map.delete(key),
    setItem: (key: string, value: string) => void map.set(key, String(value)),
  } as Storage;
}

/**
 * Replaces `window.localStorage` / `window.sessionStorage` with an in-memory
 * double. Returns the restore function — call it in `afterEach`.
 */
export function installMemoryStorage(
  kind: StorageKind,
  seed: Record<string, string> = {},
): () => void {
  return install(kind, memoryStorage(seed));
}

/**
 * A storage double that throws on every access, matching Safari private mode
 * and quota-exhausted browsers. Used to prove callers swallow the failure.
 */
export function installThrowingStorage(kind: StorageKind): () => void {
  const boom = () => {
    throw new DOMException('QuotaExceededError');
  };

  return install(kind, {
    get length(): number {
      return boom();
    },
    clear: boom,
    getItem: boom,
    key: boom,
    removeItem: boom,
    setItem: boom,
  } as unknown as Storage);
}
