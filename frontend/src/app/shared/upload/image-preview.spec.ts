import { LocalPreviews, createPreviewUrl, revokePreviewUrl } from './image-preview';

describe('image previews', () => {
  let revoke: jasmine.Spy;

  beforeEach(() => {
    revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();
  });

  describe('createPreviewUrl', () => {
    it('makes an object URL for the file', () => {
      const url = createPreviewUrl(new Blob(['x']));

      expect(url).toMatch(/^blob:/);
      revokePreviewUrl(url);
    });

    it('returns null where object URLs are unavailable', () => {
      const original = Object.getOwnPropertyDescriptor(URL, 'createObjectURL')!;
      Object.defineProperty(URL, 'createObjectURL', { value: undefined, configurable: true });

      try {
        expect(createPreviewUrl(new Blob(['x']))).toBeNull();
      } finally {
        Object.defineProperty(URL, 'createObjectURL', original);
      }
    });
  });

  describe('revokePreviewUrl', () => {
    it('revokes an object URL', () => {
      revokePreviewUrl('blob:http://localhost/abc');

      expect(revoke).toHaveBeenCalledOnceWith('blob:http://localhost/abc');
    });

    it('ignores null, undefined and remote URLs', () => {
      revokePreviewUrl(null);
      revokePreviewUrl(undefined);
      revokePreviewUrl('https://cdn/a.png');

      expect(revoke).not.toHaveBeenCalled();
    });
  });

  describe('LocalPreviews', () => {
    let previews: LocalPreviews;

    beforeEach(() => (previews = new LocalPreviews()));

    it('falls back to the remote URL when there is no preview', () => {
      expect(previews.srcFor('https://cdn/a.png')).toBe('https://cdn/a.png');
    });

    it('stands an adopted preview in for its remote URL', () => {
      previews.adopt('https://cdn/a.png', 'blob:a');

      expect(previews.srcFor('https://cdn/a.png')).toBe('blob:a');
    });

    it('ignores a null preview', () => {
      previews.adopt('https://cdn/a.png', null);

      expect(previews.srcFor('https://cdn/a.png')).toBe('https://cdn/a.png');
    });

    it('revokes the old preview when one is adopted over it', () => {
      previews.adopt('https://cdn/a.png', 'blob:old');
      previews.adopt('https://cdn/a.png', 'blob:new');

      expect(revoke).toHaveBeenCalledOnceWith('blob:old');
      expect(previews.srcFor('https://cdn/a.png')).toBe('blob:new');
    });

    it('revokes a released preview', () => {
      previews.adopt('https://cdn/a.png', 'blob:a');
      previews.release('https://cdn/a.png');

      expect(revoke).toHaveBeenCalledOnceWith('blob:a');
      expect(previews.srcFor('https://cdn/a.png')).toBe('https://cdn/a.png');
    });

    it('revokes only the previews of images that are gone', () => {
      previews.adopt('https://cdn/a.png', 'blob:a');
      previews.adopt('https://cdn/b.png', 'blob:b');

      previews.retainOnly(['https://cdn/b.png']);

      expect(revoke).toHaveBeenCalledOnceWith('blob:a');
      expect(previews.srcFor('https://cdn/b.png')).toBe('blob:b');
    });

    it('revokes everything on releaseAll', () => {
      previews.adopt('https://cdn/a.png', 'blob:a');
      previews.adopt('https://cdn/b.png', 'blob:b');

      previews.releaseAll();

      expect(revoke.calls.allArgs()).toEqual([['blob:a'], ['blob:b']]);
    });
  });
});
