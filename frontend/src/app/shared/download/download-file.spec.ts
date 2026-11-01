import { fakeAsync, tick } from '@angular/core/testing';

import { DOWNLOAD_URL_LIFETIME_MS, downloadFile } from './download-file';

describe('downloadFile', () => {
  it('clicks an attached anchor, removes it, and revokes the URL only after a moment', fakeAsync(() => {
    const createObjectURL = spyOn(URL, 'createObjectURL').and.returnValue('blob:file');
    const revokeObjectURL = spyOn(URL, 'revokeObjectURL');
    let attachedWhenClicked = false;
    const click = spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (
      this: HTMLAnchorElement,
    ) {
      attachedWhenClicked = document.body.contains(this);
    });

    expect(downloadFile('hello', 'greeting.txt', 'text/plain')).toBeTrue();

    const anchor = click.calls.mostRecent().object as HTMLAnchorElement;
    expect(attachedWhenClicked).toBeTrue();
    expect(anchor.download).toBe('greeting.txt');
    expect(anchor.href).toBe('blob:file');
    expect(document.body.contains(anchor)).toBeFalse();
    expect((createObjectURL.calls.mostRecent().args[0] as Blob).type).toBe('text/plain');

    expect(revokeObjectURL).not.toHaveBeenCalled();
    tick(DOWNLOAD_URL_LIFETIME_MS);
    expect(revokeObjectURL).toHaveBeenCalledOnceWith('blob:file');
  }));

  it('still cleans up when the click throws', fakeAsync(() => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:file');
    const revokeObjectURL = spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click').and.throwError('blocked');
    const before = document.body.querySelectorAll('a').length;

    expect(() => downloadFile('x', 'x.txt', 'text/plain')).toThrowError('blocked');

    expect(document.body.querySelectorAll('a').length).toBe(before);
    tick(DOWNLOAD_URL_LIFETIME_MS);
    expect(revokeObjectURL).toHaveBeenCalledOnceWith('blob:file');
  }));

  it('does nothing without object URL support', () => {
    const original = URL.createObjectURL;
    (URL as unknown as { createObjectURL?: unknown }).createObjectURL = undefined;
    try {
      expect(downloadFile('x', 'x.txt', 'text/plain')).toBeFalse();
    } finally {
      URL.createObjectURL = original;
    }
  });
});
