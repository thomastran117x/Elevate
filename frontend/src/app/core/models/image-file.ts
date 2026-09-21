/**
 * Client-side screening for image uploads, shared by every editor that picks an image file.
 *
 * This only decides whether an upload is worth attempting. The server is what enforces the rule:
 * it identifies an image by its bytes, so nothing here can make a file acceptable.
 */

/**
 * The extensions the server accepts. Some systems report no MIME type at all for a real image —
 * a `.webp` with no mapping, say — and `File.type` is then the empty string. The upload service
 * sends such a file as `application/octet-stream` and the server works the type out from the
 * extension or the bytes, so an untyped file is judged by its name rather than turned away.
 */
const IMAGE_EXTENSION = /\.(jpe?g|png|webp|gif)$/i;

/** Whether a picked file is plausibly an image the server will accept. */
export function looksLikeImage(file: File): boolean {
  return file.type ? file.type.startsWith('image/') : IMAGE_EXTENSION.test(file.name);
}
