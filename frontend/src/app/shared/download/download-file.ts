/** How long the object URL outlives the click, so a browser that starts the download asynchronously still finds it. */
export const DOWNLOAD_URL_LIFETIME_MS = 1_000;

/**
 * Saves generated content as a file through a temporary object URL. The anchor is attached to the
 * page for the click, which some browsers require, and the URL is revoked a moment later rather
 * than at once, which could cancel a download that has not started yet.
 *
 * Returns false, doing nothing, where there is no DOM or object URL support (server rendering).
 */
export function downloadFile(content: BlobPart, fileName: string, type: string): boolean {
  if (
    typeof document === 'undefined' ||
    typeof URL === 'undefined' ||
    typeof URL.createObjectURL !== 'function'
  ) {
    return false;
  }

  const href = URL.createObjectURL(new Blob([content], { type }));
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  try {
    anchor.click();
  } finally {
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(href), DOWNLOAD_URL_LIFETIME_MS);
  }
  return true;
}
