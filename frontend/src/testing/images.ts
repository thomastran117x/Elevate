export type TestImageFormat = 'png' | 'jpeg' | 'webp' | 'gif';

/** A 1×1 GIF, since a canvas cannot encode one. */
const GIF_1X1 = 'R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

/**
 * A real, decodable image, for specs that exercise the upload screening: it sniffs bytes and
 * decodes the image, so a `new File(['bytes'], 'a.png')` no longer passes for one.
 *
 * The format defaults to whatever `type` names, so the bytes and the declared type agree unless a
 * spec asks otherwise; pass an empty `type` for a file the browser could not type.
 */
export async function imageFile(
  name = 'image.png',
  type = 'image/png',
  options: { format?: TestImageFormat; width?: number; height?: number } = {},
): Promise<File> {
  const format = options.format ?? formatOf(type);
  const blob =
    format === 'gif'
      ? new Blob([Uint8Array.from(atob(GIF_1X1), (char) => char.charCodeAt(0))])
      : await encode(format, options.width ?? 1, options.height ?? 1);

  return new File([blob], name, { type });
}

function encode(format: TestImageFormat, width: number, height: number): Promise<Blob> {
  const canvas = new OffscreenCanvas(width, height);
  // A canvas with no context has nothing to encode.
  canvas.getContext('2d')!.fillRect(0, 0, 1, 1);
  return canvas.convertToBlob({ type: `image/${format}` });
}

/** A file with exactly these bytes, for signature checks that must not decode anything. */
export function bytesFile(name: string, type: string, bytes: ArrayLike<number> | string): File {
  const content =
    typeof bytes === 'string' ? Uint8Array.from(bytes, (c) => c.charCodeAt(0)) : bytes;
  return new File([Uint8Array.from(content)], name, { type });
}

function formatOf(type: string): TestImageFormat {
  switch (type) {
    case 'image/jpeg':
    case 'image/jpg':
      return 'jpeg';
    case 'image/webp':
      return 'webp';
    case 'image/gif':
      return 'gif';
    default:
      return 'png';
  }
}
