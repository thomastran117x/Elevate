import { bytesFile, imageFile } from '@testing';

import {
  ALLOWED_IMAGE_EXTENSIONS,
  ALLOWED_IMAGE_TYPES,
  IMAGE_ACCEPT,
  ImageFormat,
  MAX_IMAGE_BYTES,
  MAX_IMAGE_DIMENSION,
  probeImageDimensions,
  screenImageFile,
  sniffImageFormat,
  validateImageFile,
} from './image-file-validation';

const PNG_MAGIC = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

describe('image file validation', () => {
  const sized = (name: string, type: string, size: number) =>
    new File([new Uint8Array(size)], name, { type });

  describe('IMAGE_ACCEPT', () => {
    it('lists every allowed type and extension, and nothing broader', () => {
      for (const entry of [...ALLOWED_IMAGE_TYPES, ...ALLOWED_IMAGE_EXTENSIONS]) {
        expect(IMAGE_ACCEPT.split(',')).toContain(entry);
      }
      expect(IMAGE_ACCEPT).not.toContain('image/*');
    });
  });

  describe('validateImageFile', () => {
    it('accepts every allowed type, and the image/jpg alias', () => {
      for (const type of [...ALLOWED_IMAGE_TYPES, 'image/jpg', 'IMAGE/PNG']) {
        expect(validateImageFile(sized('photo', type, 10)))
          .withContext(type)
          .toEqual({ ok: true });
      }
    });

    it('rejects image types the server does not store, naming the file', () => {
      for (const type of ['image/svg+xml', 'image/bmp', 'image/heic', 'image/tiff']) {
        const result = validateImageFile(sized('logo.png', type, 10));

        expect(result).withContext(type).toEqual({
          ok: false,
          reason: 'type',
          message: `"logo.png" isn't a supported image. Use JPG, PNG, WEBP or GIF.`,
        });
      }
    });

    it('rejects a declared non-image type whatever the name', () => {
      expect(validateImageFile(sized('fake.png', 'text/plain', 10))).toEqual(
        jasmine.objectContaining({ ok: false, reason: 'type' }),
      );
      // A property of Object.prototype is not an allowed type.
      expect(validateImageFile(sized('x.png', 'constructor', 10))).toEqual(
        jasmine.objectContaining({ ok: false, reason: 'type' }),
      );
    });

    it('judges an untyped file by its extension', () => {
      for (const name of ['a.jpg', 'b.JPEG', 'c.png', 'd.WebP', 'e.gif']) {
        expect(validateImageFile(sized(name, '', 10)))
          .withContext(name)
          .toEqual({ ok: true });
      }
      for (const name of ['notes', 'archive.zip', 'image.bmp', 'png', 'photo.png.exe']) {
        expect(validateImageFile(sized(name, '', 10)))
          .withContext(name)
          .toEqual(jasmine.objectContaining({ ok: false, reason: 'type' }));
      }
    });

    it('ignores the name when the type is declared', () => {
      expect(validateImageFile(sized('photo.png', 'image/jpeg', 10))).toEqual({ ok: true });
    });

    it('rejects a zero-byte file', () => {
      expect(validateImageFile(sized('blank.png', 'image/png', 0))).toEqual({
        ok: false,
        reason: 'empty',
        message: '"blank.png" is empty.',
      });
    });

    it('accepts a file exactly at the cap and rejects one byte over', () => {
      expect(validateImageFile(sized('max.png', 'image/png', MAX_IMAGE_BYTES))).toEqual({
        ok: true,
      });
      expect(validateImageFile(sized('huge.png', 'image/png', MAX_IMAGE_BYTES + 1))).toEqual({
        ok: false,
        reason: 'too-large',
        message: '"huge.png" is larger than 5MB.',
      });
    });
  });

  describe('sniffImageFormat', () => {
    it('recognises each signature the server accepts', async () => {
      const cases: [number[] | string, ImageFormat][] = [
        [[0xff, 0xd8, 0xff, 0xe0], 'jpeg'],
        [PNG_MAGIC, 'png'],
        ['GIF87a', 'gif'],
        ['GIF89a', 'gif'],
        ['RIFF\u0000\u0000\u0000\u0000WEBPVP8 ', 'webp'],
      ];

      for (const [bytes, format] of cases) {
        expect(await sniffImageFormat(bytesFile('f', '', bytes)))
          .withContext(format)
          .toBe(format);
      }
    });

    it('rejects a RIFF container that is not WEBP', async () => {
      expect(
        await sniffImageFormat(bytesFile('f', '', 'RIFF\u0000\u0000\u0000\u0000WAVEfmt ')),
      ).toBe(null);
    });

    it('rejects a header too short to hold a signature', async () => {
      expect(await sniffImageFormat(bytesFile('f', '', [0xff, 0xd8]))).toBeNull();
      expect(await sniffImageFormat(bytesFile('f', '', PNG_MAGIC.slice(0, 7)))).toBeNull();
      expect(
        await sniffImageFormat(bytesFile('f', '', 'RIFF\u0000\u0000\u0000\u0000WEB')),
      ).toBeNull();
    });

    it('rejects text and other formats', async () => {
      expect(await sniffImageFormat(bytesFile('f', '', 'hello, world'))).toBeNull();
      expect(await sniffImageFormat(bytesFile('f', '', '<svg xmlns="x"/>'))).toBeNull();
      expect(await sniffImageFormat(bytesFile('f', '', 'BM6\u0000'))).toBeNull();
    });

    it('treats an unreadable file as unrecognised', async () => {
      const unreadable = {
        slice: () => ({ arrayBuffer: () => Promise.reject(new Error('NotReadableError')) }),
      } as unknown as Blob;

      expect(await sniffImageFormat(unreadable)).toBeNull();
    });
  });

  describe('probeImageDimensions', () => {
    it('reads the decoded size and closes the bitmap', async () => {
      const close = jasmine.createSpy('close');
      spyOn(window, 'createImageBitmap').and.resolveTo({
        width: 3,
        height: 4,
        close,
      } as unknown as ImageBitmap);

      expect(await probeImageDimensions(new Blob())).toEqual({ width: 3, height: 4 });
      expect(close).toHaveBeenCalled();
    });
  });

  describe('screenImageFile', () => {
    it('accepts a real image of each allowed type', async () => {
      for (const type of ALLOWED_IMAGE_TYPES) {
        expect(await screenImageFile(await imageFile('photo', type)))
          .withContext(type)
          .toEqual({ ok: true });
      }
    });

    it('stops at the metadata checks before reading any bytes', async () => {
      const svg = bytesFile('logo.svg', 'image/svg+xml', '<svg/>');
      const read = spyOn(svg, 'slice').and.callThrough();

      expect(await screenImageFile(svg)).toEqual(jasmine.objectContaining({ reason: 'type' }));
      expect(read).not.toHaveBeenCalled();
    });

    it('rejects a text file named and typed as a PNG', async () => {
      expect(await screenImageFile(bytesFile('notes.png', 'image/png', 'just some text'))).toEqual({
        ok: false,
        reason: 'signature',
        message: `"notes.png" isn't a JPG, PNG, WEBP or GIF image.`,
      });
    });

    it('rejects bytes that contradict the declared type', async () => {
      const gif = await imageFile('photo.png', 'image/png', { format: 'gif' });

      expect(await screenImageFile(gif)).toEqual({
        ok: false,
        reason: 'mismatch',
        message: `"photo.png" doesn't match its file type.`,
      });
    });

    it('accepts a .png name declared as JPEG when the bytes are JPEG', async () => {
      expect(await screenImageFile(await imageFile('photo.png', 'image/jpeg'))).toEqual({
        ok: true,
      });
    });

    it('skips the type cross-check for an untyped file, as the server does', async () => {
      expect(await screenImageFile(await imageFile('photo.WEBP', '', { format: 'webp' }))).toEqual({
        ok: true,
      });
    });

    it('rejects a file with a valid header that does not decode', async () => {
      const corrupt = bytesFile('broken.png', 'image/png', [...PNG_MAGIC, 1, 2, 3, 4, 5, 6, 7, 8]);

      expect(await screenImageFile(corrupt)).toEqual({
        ok: false,
        reason: 'undecodable',
        message: `"broken.png" couldn't be read as an image.`,
      });
    });

    it('rejects an image wider or taller than the cap', async () => {
      const wide = await imageFile('wide.png', 'image/png', { width: MAX_IMAGE_DIMENSION + 1 });
      const tall = await imageFile('tall.png', 'image/png', { height: MAX_IMAGE_DIMENSION + 1 });

      expect(await screenImageFile(wide)).toEqual({
        ok: false,
        reason: 'dimensions',
        message: `"wide.png" is larger than 8192 × 8192 pixels.`,
      });
      expect(await screenImageFile(tall)).toEqual(
        jasmine.objectContaining({ reason: 'dimensions' }),
      );
    });

    it('accepts an image exactly at the cap', async () => {
      const edge = await imageFile('edge.png', 'image/png', {
        width: MAX_IMAGE_DIMENSION,
        height: 1,
      });

      expect(await screenImageFile(edge)).toEqual({ ok: true });
    });

    it('skips the decode where the platform cannot decode', async () => {
      const original = Object.getOwnPropertyDescriptor(window, 'createImageBitmap')!;
      Object.defineProperty(window, 'createImageBitmap', { value: undefined, configurable: true });

      try {
        const corrupt = bytesFile('broken.png', 'image/png', [...PNG_MAGIC, 0, 0, 0, 0]);
        expect(await screenImageFile(corrupt)).toEqual({ ok: true });
      } finally {
        Object.defineProperty(window, 'createImageBitmap', original);
      }
    });
  });
});
