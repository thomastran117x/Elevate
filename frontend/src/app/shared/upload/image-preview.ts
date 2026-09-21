/**
 * Local previews for picked images, rendered from the file itself rather than from wherever it
 * was uploaded to. The user sees the image the moment it is picked, and the editor does not
 * depend on the uploaded blob being publicly readable yet.
 *
 * The contract: every URL {@link createPreviewUrl} returns holds the whole file in memory until
 * it is revoked, and nothing revokes it for you. Whoever creates one revokes it exactly once —
 * when it is swapped for another pick, when its image is removed, when its upload fails, and when
 * the owning component is destroyed.
 */

/** An object URL for the file, or null where the platform cannot make one (SSR). */
export function createPreviewUrl(file: Blob): string | null {
  return typeof URL.createObjectURL === 'function' ? URL.createObjectURL(file) : null;
}

/**
 * Releases a URL made by {@link createPreviewUrl}. Anything that is not an object URL — null, or
 * the remote URL an editor falls back to — is ignored, so callers need not tell them apart.
 */
export function revokePreviewUrl(url: string | null | undefined): void {
  if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
}

/**
 * Local previews for a gallery, keyed by the remote URL each upload produced, so a template can
 * render the local file in place of an uploaded image. Owns every URL it is given.
 */
export class LocalPreviews {
  private readonly previews = new Map<string, string>();

  /** The local preview standing in for `remoteUrl`, or `remoteUrl` itself when there is none. */
  srcFor(remoteUrl: string): string {
    return this.previews.get(remoteUrl) ?? remoteUrl;
  }

  /** Takes ownership of `previewUrl` as the stand-in for `remoteUrl`. */
  adopt(remoteUrl: string, previewUrl: string | null): void {
    if (!previewUrl) return;
    this.release(remoteUrl);
    this.previews.set(remoteUrl, previewUrl);
  }

  /** Revokes the preview for one image, after it is removed. */
  release(remoteUrl: string): void {
    revokePreviewUrl(this.previews.get(remoteUrl));
    this.previews.delete(remoteUrl);
  }

  /** Revokes the preview of every image not in `remoteUrls`. */
  retainOnly(remoteUrls: readonly string[]): void {
    const keep = new Set(remoteUrls);
    for (const remoteUrl of [...this.previews.keys()]) {
      if (!keep.has(remoteUrl)) this.release(remoteUrl);
    }
  }

  /** Revokes everything; call on destroy. */
  releaseAll(): void {
    this.retainOnly([]);
  }
}
