import { bytesFile, imageFile } from '@testing';

import {
  ALLOWED_IMAGE_EXTENSIONS,
  ALLOWED_IMAGE_TYPES,
  IMAGE_ACCEPT,
  ImageFormat,
  isAnimatedImage,
  MAX_IMAGE_BYTES,
  MAX_IMAGE_DIMENSION,
  probeImageDimensions,
  screenImageFile,
  sniffImageFormat,
  validateImageFile,
} from './image-file-validation';

const PNG_MAGIC = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

const asciiBytes = (text: string) => Array.from(text, (c) => c.charCodeAt(0));

/**
 * A GIF with the given number of 1×1 frames, a global colour table, and a graphic control
 * extension before each frame, so the walk has to skip extensions and colour tables to count.
 */
function gifBytes(frames: number): Uint8Array<ArrayBuffer> {
  const bytes = [
    ...asciiBytes('GIF89a'),
    0x01,
    0x00,
    0x01,
    0x00, // logical screen 1×1
    0x80,
    0x00,
    0x00, //       global colour table of 2 entries
    0x00,
    0x00,
    0x00,
    0xff,
    0xff,
    0xff,
  ];
  for (let i = 0; i < frames; i++) {
    bytes.push(0x21, 0xf9, 0x04, 0x00, 0x0a, 0x00, 0x00, 0x00); // graphic control extension
    bytes.push(0x2c, 0, 0, 0, 0, 0x01, 0x00, 0x01, 0x00, 0x00); // image descriptor
    bytes.push(0x02, 0x02, 0x4c, 0x01, 0x00); //                    LZW size, one sub-block, end
  }
  bytes.push(0x3b);
  return new Uint8Array(bytes);
}

/** PNG chunk layout only; nothing here checks CRCs, so they are left zero. */
function pngBytes(chunks: string[]): Uint8Array<ArrayBuffer> {
  const bytes = [...PNG_MAGIC];
  for (const type of chunks) {
    const data = type === 'IHDR' ? 13 : type === 'acTL' ? 8 : 0;
    bytes.push(0, 0, 0, data, ...asciiBytes(type), ...new Array(data).fill(0), 0, 0, 0, 0);
  }
  return new Uint8Array(bytes);
}

function webpBytes(chunk: 'VP8X' | 'VP8 ', flags = 0): Uint8Array<ArrayBuffer> {
  const bytes = [
    ...asciiBytes('RIFF'),
    30,
    0,
    0,
    0,
    ...asciiBytes('WEBP'),
    ...asciiBytes(chunk),
    10,
    0,
    0,
    0,
  ];
  bytes.push(flags, 0, 0, 0, 0, 0, 0, 0, 0, 0);
  return new Uint8Array(bytes);
}

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

    it('treats application/octet-stream as no type, as the server does', () => {
      expect(validateImageFile(sized('photo.webp', 'application/octet-stream', 10))).toEqual({
        ok: true,
      });
      expect(validateImageFile(sized('notes.txt', 'application/octet-stream', 10))).toEqual(
        jasmine.objectContaining({ ok: false, reason: 'type' }),
      );
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

  describe('isAnimatedImage', () => {
    const blob = (bytes: Uint8Array<ArrayBuffer>) => new Blob([bytes]);

    it('spots a GIF with more than one frame, past its extensions and colour tables', async () => {
      expect(await isAnimatedImage(blob(gifBytes(2)), 'gif')).toBeTrue();
      expect(await isAnimatedImage(blob(gifBytes(1)), 'gif')).toBeFalse();
    });

    it('spots an APNG by its animation control chunk before the first IDAT', async () => {
      expect(
        await isAnimatedImage(blob(pngBytes(['IHDR', 'acTL', 'IDAT', 'IEND'])), 'png'),
      ).toBeTrue();
      expect(await isAnimatedImage(blob(pngBytes(['IHDR', 'IDAT', 'IEND'])), 'png')).toBeFalse();
      // An acTL after the image data is not an APNG.
      expect(
        await isAnimatedImage(blob(pngBytes(['IHDR', 'IDAT', 'acTL', 'IEND'])), 'png'),
      ).toBeFalse();
    });

    it('spots a WebP whose extended header sets the animation flag', async () => {
      expect(await isAnimatedImage(blob(webpBytes('VP8X', 0x02)), 'webp')).toBeTrue();
      expect(await isAnimatedImage(blob(webpBytes('VP8X', 0x10)), 'webp')).toBeFalse();
      expect(await isAnimatedImage(blob(webpBytes('VP8 ')), 'webp')).toBeFalse();
    });

    it('never calls a JPEG animated', async () => {
      expect(
        await isAnimatedImage(blob(new Uint8Array([0xff, 0xd8, 0xff, 0xe0])), 'jpeg'),
      ).toBeFalse();
    });

    it('leaves a file too malformed to walk to the decoder and the server', async () => {
      expect(await isAnimatedImage(blob(gifBytes(2).slice(0, 12)), 'gif')).toBeFalse();
      expect(
        await isAnimatedImage(
          blob(new Uint8Array([...asciiBytes('GIF89a'), 1, 0, 1, 0, 0, 0, 0, 0x99])),
          'gif',
        ),
      ).toBeFalse();
      expect(await isAnimatedImage(blob(new Uint8Array(PNG_MAGIC)), 'png')).toBeFalse();
    });

    it('reports a real still image as not animated', async () => {
      expect(await isAnimatedImage(await imageFile('still.png'), 'png')).toBeFalse();
    });

    it('treats a file that cannot be read as not animated', async () => {
      const unreadable = {
        slice: () => unreadable,
        arrayBuffer: () => Promise.reject(new Error('gone')),
      } as unknown as Blob;

      expect(await isAnimatedImage(unreadable, 'gif')).toBeFalse();
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
    it('refuses an animated image by name, as the server would after the upload', async () => {
      const result = await screenImageFile(
        new File([gifBytes(3)], 'loop.gif', { type: 'image/gif' }),
      );

      expect(result).toEqual({
        ok: false,
        reason: 'animated',
        message: '"loop.gif" is animated. Upload a single-frame image.',
      });
    });

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

    it('accepts a real image the browser typed as application/octet-stream', async () => {
      const opaque = await imageFile('photo.png', 'application/octet-stream', { format: 'png' });

      expect(await screenImageFile(opaque)).toEqual({ ok: true });
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
