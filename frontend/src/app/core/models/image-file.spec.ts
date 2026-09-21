import { looksLikeImage } from './image-file';

describe('looksLikeImage', () => {
  const file = (name: string, type: string) => new File(['bytes'], name, { type });

  it('accepts any file the browser reports as an image', () => {
    expect(looksLikeImage(file('photo.png', 'image/png'))).toBeTrue();
    expect(looksLikeImage(file('scan', 'image/webp'))).toBeTrue();
  });

  it('rejects a file the browser reports as something else, whatever its name', () => {
    // A declared type is taken at its word; the name only matters when there is no type.
    expect(looksLikeImage(file('notes.pdf', 'application/pdf'))).toBeFalse();
    expect(looksLikeImage(file('fake.png', 'text/plain'))).toBeFalse();
  });

  it('falls back to the extension when the browser reports no type', () => {
    for (const name of ['a.jpg', 'b.JPEG', 'c.png', 'd.WebP', 'e.gif']) {
      expect(looksLikeImage(file(name, '')))
        .withContext(name)
        .toBeTrue();
    }
  });

  it('rejects an untyped file without a supported image extension', () => {
    for (const name of ['notes', 'archive.zip', 'image.bmp', 'png', 'photo.png.exe']) {
      expect(looksLikeImage(file(name, '')))
        .withContext(name)
        .toBeFalse();
    }
  });
});
