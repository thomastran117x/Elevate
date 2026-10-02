import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { IMAGE_FALLBACK_SRC, ImageFallbackDirective } from './image-fallback.directive';

@Component({
  standalone: true,
  imports: [ImageFallbackDirective],
  template: `<img appImageFallback [src]="src" alt="" />`,
})
class HostComponent {
  src = 'https://cdn/events/not-published-yet.webp';
}

describe('ImageFallbackDirective', () => {
  function render() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('img') as HTMLImageElement;
  }

  it('swaps an image that fails to load for the placeholder', () => {
    const image = render();

    image.dispatchEvent(new Event('error'));

    expect(image.src).toBe(IMAGE_FALLBACK_SRC);
  });

  it('does not loop when the placeholder itself fails', () => {
    const image = render();
    image.dispatchEvent(new Event('error'));
    const setter = spyOnProperty(HTMLImageElement.prototype, 'src', 'set').and.callThrough();

    image.dispatchEvent(new Event('error'));

    expect(setter).not.toHaveBeenCalled();
  });

  it('leaves an image that loads alone', () => {
    const image = render();

    image.dispatchEvent(new Event('load'));

    expect(image.src).toBe('https://cdn/events/not-published-yet.webp');
  });
});
