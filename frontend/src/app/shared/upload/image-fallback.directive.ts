import { Directive, ElementRef, inject } from '@angular/core';

/**
 * A neutral tile, drawn inline so it cannot itself fail to load. Theme-agnostic grey at partial
 * opacity, so it reads as "no image" on light and dark surfaces alike.
 */
export const IMAGE_FALLBACK_SRC =
  'data:image/svg+xml;charset=utf-8,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">' +
      '<rect width="64" height="64" fill="#94a3b8" fill-opacity="0.18"/>' +
      '<path d="M18 44l10-12 8 9 6-7 8 10z" fill="#94a3b8" fill-opacity="0.6"/>' +
      '<circle cx="42" cy="24" r="4" fill="#94a3b8" fill-opacity="0.6"/>' +
      '</svg>',
  );

/**
 * Swaps an image that fails to load for a neutral placeholder instead of the browser's broken
 * image icon. For editor tiles whose public URL may not have anything behind it yet: a quarantined
 * upload is only published once it has been validated.
 *
 * Swaps once. If the placeholder itself is what failed, it is left alone rather than looping.
 */
@Directive({
  selector: 'img[appImageFallback]',
  standalone: true,
  host: {
    '(error)': 'onError()',
  },
})
export class ImageFallbackDirective {
  private readonly image = inject<ElementRef<HTMLImageElement>>(ElementRef).nativeElement;

  onError(): void {
    if (this.image.src === IMAGE_FALLBACK_SRC) return;
    this.image.src = IMAGE_FALLBACK_SRC;
  }
}
