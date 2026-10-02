/**
 * Client-side screening for image uploads, shared by every editor that picks an image file.
 *
 * This is UX, not security. It exists so a user who picks an iPhone HEIC, an SVG logo or a
 * renamed text file hears about it at once, by file name, instead of after a multi-megabyte
 * upload. The server is authoritative: it identifies an image by its own bytes and rejects
 * anything else, and nothing here can make a file acceptable. The two checks do different jobs,
 * so neither is redundant with the other and neither should be deleted on that basis.
 */

/** An image format the server stores. Mirrors the backend's `ImageFormat` enum. */
export type ImageFormat = 'jpeg' | 'png' | 'webp' | 'gif';

/** The canonical content types the server accepts, in the order the UI lists them. */
export const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'] as const;

/** Extensions the server accepts; they decide the matter for a file the browser could not type. */
export const ALLOWED_IMAGE_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp', '.gif'] as const;

/**
 * The `accept` attribute for every image picker. Extensions are listed alongside the types so
 * the dialog still offers a real image the OS has no MIME mapping for.
 */
export const IMAGE_ACCEPT = [...ALLOWED_IMAGE_TYPES, ...ALLOWED_IMAGE_EXTENSIONS].join(',');

/** Matches the server's `ImageUpload:MaxBytes` and `AvatarUploadRequest.MaxImageBytes`. */
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

/**
 * Largest width or height screened in. The server has no pixel cap yet; this is the value the
 * media worker is expected to adopt, and the two should be kept equal once it does.
 */
export const MAX_IMAGE_DIMENSION = 8192;

export type ImageFileRejection =
  | 'type'
  | 'empty'
  | 'too-large'
  | 'signature'
  | 'mismatch'
  | 'animated'
  | 'undecodable'
  | 'dimensions';

export type ImageFileResult =
  { ok: true } | { ok: false; reason: ImageFileRejection; message: string };

/**
 * Declared types mapped to the format their bytes must carry. `image/jpg` is not a registered
 * type but some systems send it, and the server's presigned endpoint accepts it as JPEG.
 */
const DECLARED_FORMATS: Readonly<Record<string, ImageFormat>> = {
  'image/jpeg': 'jpeg',
  'image/jpg': 'jpeg',
  'image/png': 'png',
  'image/webp': 'webp',
  'image/gif': 'gif',
};

const IMAGE_EXTENSION = /\.(jpe?g|png|webp|gif)$/i;

const SUPPORTED_LIST = 'JPG, PNG, WEBP or GIF';

const ACCEPTED: ImageFileResult = { ok: true };

const MESSAGES: Record<ImageFileRejection, (name: string) => string> = {
  type: (name) => `"${name}" isn't a supported image. Use ${SUPPORTED_LIST}.`,
  empty: (name) => `"${name}" is empty.`,
  'too-large': (name) => `"${name}" is larger than ${MAX_IMAGE_BYTES / (1024 * 1024)}MB.`,
  signature: (name) => `"${name}" isn't a ${SUPPORTED_LIST} image.`,
  mismatch: (name) => `"${name}" doesn't match its file type.`,
  animated: (name) => `"${name}" is animated. Upload a single-frame image.`,
  undecodable: (name) => `"${name}" couldn't be read as an image.`,
  dimensions: (name) =>
    `"${name}" is larger than ${MAX_IMAGE_DIMENSION} × ${MAX_IMAGE_DIMENSION} pixels.`,
};

/**
 * The type the browser declared, or the empty string when it declared nothing useful. Some systems
 * report `application/octet-stream` for a real image they have no mapping for; the server treats
 * that exactly like no type at all (`AzureBlobService.ResolveImageContentType`), so this does too.
 */
function declaredType(file: File): string {
  const type = file.type.toLowerCase();
  return type === 'application/octet-stream' ? '' : type;
}

function reject(file: File, reason: ImageFileRejection): ImageFileResult {
  return { ok: false, reason, message: MESSAGES[reason](file.name) };
}

/**
 * The checks that need only the file's metadata. A declared type is taken at its word and the
 * name is ignored. An untyped file — a `.webp` the OS has no mapping for, say, reported with no
 * type or as `application/octet-stream` — is judged by its extension instead of being turned away,
 * because the upload service sends it as `application/octet-stream` and the server works the type
 * out itself.
 */
export function validateImageFile(file: File): ImageFileResult {
  const type = declaredType(file);
  const known = type ? Object.hasOwn(DECLARED_FORMATS, type) : IMAGE_EXTENSION.test(file.name);

  if (!known) return reject(file, 'type');
  if (file.size === 0) return reject(file, 'empty');
  if (file.size > MAX_IMAGE_BYTES) return reject(file, 'too-large');
  return ACCEPTED;
}

const PNG_MAGIC = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const GIF87A = ascii('GIF87a');
const GIF89A = ascii('GIF89a');
const RIFF = ascii('RIFF');
const WEBP = ascii('WEBP');

function ascii(text: string): number[] {
  return Array.from(text, (char) => char.charCodeAt(0));
}

function startsWith(bytes: Uint8Array, magic: readonly number[], offset = 0): boolean {
  return bytes.length >= offset + magic.length && magic.every((b, i) => bytes[offset + i] === b);
}

/**
 * Identifies an image by its leading bytes, or returns null when they match no supported format
 * or cannot be read.
 *
 * The signature table is a copy of the backend's `ImageSignatureInspector.TryDetect`, and that
 * file points back here: a format added to or removed from either must change both, or this
 * screen starts turning away files the server takes, or passing ones it refuses.
 */
export async function sniffImageFormat(file: Blob): Promise<ImageFormat | null> {
  let bytes: Uint8Array;
  try {
    bytes = new Uint8Array(await file.slice(0, 32).arrayBuffer());
  } catch {
    // The file vanished or became unreadable after it was picked.
    return null;
  }

  if (startsWith(bytes, [0xff, 0xd8, 0xff])) return 'jpeg';
  if (startsWith(bytes, PNG_MAGIC)) return 'png';
  if (startsWith(bytes, GIF87A) || startsWith(bytes, GIF89A)) return 'gif';
  // RIFF is a container: WAV and AVI open with the same four bytes, so the form type at offset 8
  // is what actually says "this is a WebP".
  if (startsWith(bytes, RIFF) && startsWith(bytes, WEBP, 8)) return 'webp';
  return null;
}

/**
 * Whether an image of the given format holds more than one frame. Mirrors the server's
 * `ImageSharpImageProcessor`, which refuses animated GIF, WebP and APNG because re-encoding keeps
 * only the first frame: saying so when the file is picked beats a refusal after the upload.
 *
 * Reads structure only — block, chunk and header markers — never pixel data. A file too malformed
 * to walk counts as not animated here; the decode probe and the server judge it.
 */
export async function isAnimatedImage(file: Blob, format: ImageFormat): Promise<boolean> {
  if (format === 'jpeg') return false;

  let bytes: Uint8Array;
  try {
    // WebP declares animation in its first header; GIF and PNG have to be walked.
    bytes = new Uint8Array(await (format === 'webp' ? file.slice(0, 32) : file).arrayBuffer());
  } catch {
    return false;
  }

  if (format === 'gif') return gifHasSeveralFrames(bytes);
  if (format === 'png') return pngHasAnimationControl(bytes);
  return webpIsAnimated(bytes);
}

/** Counts image descriptors past the header, stopping at the second. */
function gifHasSeveralFrames(bytes: Uint8Array): boolean {
  if (bytes.length < 13) return false;

  let offset = 13 + colorTableLength(bytes[10]);
  let frames = 0;
  while (offset < bytes.length) {
    const block = bytes[offset];
    if (block === 0x3b) break; // trailer
    if (block === 0x21) {
      // Extension: introducer, label, then data sub-blocks.
      offset = skipSubBlocks(bytes, offset + 2);
    } else if (block === 0x2c) {
      if (++frames > 1) return true;
      // Descriptor (10 bytes), optional local colour table, LZW code size, then image data.
      const flags = bytes[offset + 9] ?? 0;
      offset = skipSubBlocks(bytes, offset + 10 + colorTableLength(flags) + 1);
    } else {
      break; // malformed; leave it to the decoder
    }
  }
  return false;
}

/** Bytes in a GIF colour table described by a packed-fields byte, or 0 when there is none. */
function colorTableLength(flags: number): number {
  return flags & 0x80 ? 3 * (1 << ((flags & 0x07) + 1)) : 0;
}

function skipSubBlocks(bytes: Uint8Array, offset: number): number {
  while (offset < bytes.length) {
    const size = bytes[offset];
    offset += 1 + size;
    if (size === 0) break;
  }
  return offset;
}

/** Whether an `acTL` chunk precedes the first `IDAT`, which is what makes a PNG an APNG. */
function pngHasAnimationControl(bytes: Uint8Array): boolean {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const acTL = ascii('acTL');
  const IDAT = ascii('IDAT');
  const IEND = ascii('IEND');

  let offset = 8;
  while (offset + 8 <= bytes.length) {
    if (startsWith(bytes, acTL, offset + 4)) return true;
    if (startsWith(bytes, IDAT, offset + 4) || startsWith(bytes, IEND, offset + 4)) return false;
    // Length, type, data, CRC.
    offset += 12 + view.getUint32(offset);
  }
  return false;
}

/** Whether an extended (`VP8X`) WebP header sets its animation flag. */
function webpIsAnimated(bytes: Uint8Array): boolean {
  return startsWith(bytes, ascii('VP8X'), 12) && bytes.length > 20 && (bytes[20] & 0x02) !== 0;
}

/** Decodes the image to read its size. Rejects when the browser cannot decode it. */
export async function probeImageDimensions(file: Blob): Promise<{ width: number; height: number }> {
  const bitmap = await createImageBitmap(file);
  try {
    return { width: bitmap.width, height: bitmap.height };
  } finally {
    bitmap.close();
  }
}

/**
 * Every check, in the order the server applies its own: type and size, then the bytes, then the
 * declared type against the bytes, then animation, then whether the image decodes at a sane size. The decode also
 * catches a file with a valid header and a corrupt body before any upload starts.
 *
 * The declared-type cross-check is skipped for an untyped file, as it is on the server. Where the
 * browser cannot decode (no `createImageBitmap`), the dimension probe is skipped rather than
 * failing the file; the server still has the last word.
 */
export async function screenImageFile(file: File): Promise<ImageFileResult> {
  const basic = validateImageFile(file);
  if (!basic.ok) return basic;

  const format = await sniffImageFormat(file);
  if (!format) return reject(file, 'signature');

  const declared = DECLARED_FORMATS[declaredType(file)];
  if (declared && declared !== format) return reject(file, 'mismatch');

  if (await isAnimatedImage(file, format)) return reject(file, 'animated');

  if (typeof createImageBitmap !== 'function') return ACCEPTED;

  let size: { width: number; height: number };
  try {
    size = await probeImageDimensions(file);
  } catch {
    return reject(file, 'undecodable');
  }

  if (size.width > MAX_IMAGE_DIMENSION || size.height > MAX_IMAGE_DIMENSION) {
    return reject(file, 'dimensions');
  }
  return ACCEPTED;
}
