import { ConfirmDialogComponent } from './confirm-dialog.component';

describe('ConfirmDialogComponent', () => {
  let component: ConfirmDialogComponent;
  let resolved: boolean[];

  beforeEach(() => {
    component = new ConfirmDialogComponent();
    component.open = true;
    resolved = [];
    component.resolve.subscribe((value) => resolved.push(value));
  });

  it('resolves true when confirmed and false when dismissed', () => {
    component.confirm();
    expect(resolved).toEqual([true]);

    component.dismiss();
    expect(resolved).toEqual([true, false]);
  });

  describe('typed confirmation', () => {
    beforeEach(() => {
      component.requireTypedConfirmation = 'DELETE';
    });

    it('blocks confirming until the exact phrase is typed', () => {
      expect(component.canConfirm).toBeFalse();

      component.typedConfirmation = 'delete';
      expect(component.canConfirm).toBeFalse();

      component.typedConfirmation = 'DELETE';
      expect(component.canConfirm).toBeTrue();
    });

    it('ignores surrounding whitespace, which is usually a paste artefact', () => {
      component.typedConfirmation = '  DELETE  ';
      expect(component.canConfirm).toBeTrue();
    });

    it('does not emit while the phrase is still wrong', () => {
      component.typedConfirmation = 'DELET';
      component.confirm();

      expect(resolved).toEqual([]);
    });

    it('clears the typed phrase so a reopened dialog starts locked again', () => {
      component.typedConfirmation = 'DELETE';
      component.confirm();

      expect(component.typedConfirmation).toBe('');
      expect(component.canConfirm).toBeFalse();
    });
  });

  describe('while a request is in flight', () => {
    beforeEach(() => {
      component.busy = true;
    });

    it('refuses to confirm twice', () => {
      component.confirm();
      expect(resolved).toEqual([]);
    });

    it('refuses to dismiss, so the action cannot be abandoned mid-flight', () => {
      component.dismiss();
      component.onEscape();

      expect(resolved).toEqual([]);
    });
  });

  describe('dismissal affordances', () => {
    it('closes on Escape', () => {
      component.onEscape();
      expect(resolved).toEqual([false]);
    });

    it('ignores Escape when it is not the dialog on screen', () => {
      component.open = false;
      component.onEscape();

      expect(resolved).toEqual([]);
    });

    it('closes on a backdrop click but not on clicks bubbling out of the panel', () => {
      const backdrop = document.createElement('div');
      const panel = document.createElement('div');

      component.onBackdropClick({
        target: panel,
        currentTarget: backdrop,
      } as unknown as MouseEvent);
      expect(resolved).toEqual([]);

      component.onBackdropClick({
        target: backdrop,
        currentTarget: backdrop,
      } as unknown as MouseEvent);
      expect(resolved).toEqual([false]);
    });
  });

  describe('focus management', () => {
    function stubPanel(buttons: HTMLButtonElement[]): HTMLElement {
      const panel = document.createElement('div');
      buttons.forEach((button) => panel.appendChild(button));
      document.body.appendChild(panel);
      return panel;
    }

    let opener: HTMLButtonElement;
    let cancel: HTMLButtonElement;
    let confirm: HTMLButtonElement;
    let panel: HTMLElement;

    beforeEach(() => {
      opener = document.createElement('button');
      document.body.appendChild(opener);
      opener.focus();

      cancel = document.createElement('button');
      confirm = document.createElement('button');
      panel = stubPanel([cancel, confirm]);

      component.cancelButton = { nativeElement: cancel } as never;
      component.panel = { nativeElement: panel } as never;
    });

    afterEach(() => {
      opener.remove();
      panel.remove();
    });

    it('moves focus to the safe choice on open, never the destructive one', async () => {
      component.open = true;
      component.ngOnChanges({ open: { currentValue: true } } as never);

      await Promise.resolve();
      expect(document.activeElement).toBe(cancel);
    });

    it('hands focus back to whatever opened it on close', async () => {
      component.open = true;
      component.ngOnChanges({ open: { currentValue: true } } as never);
      await Promise.resolve();

      component.open = false;
      component.ngOnChanges({ open: { currentValue: false } } as never);

      expect(document.activeElement).toBe(opener);
    });

    it('ignores changes that are not about opening', () => {
      component.ngOnChanges({ busy: { currentValue: true } } as never);
      expect(document.activeElement).toBe(opener);
    });

    it('wraps Tab from the last control back to the first', () => {
      confirm.focus();
      const event = new KeyboardEvent('keydown', { key: 'Tab' });
      spyOn(event, 'preventDefault');

      component.onTab(event);

      expect(event.preventDefault).toHaveBeenCalled();
      expect(document.activeElement).toBe(cancel);
    });

    it('wraps Shift+Tab from the first control back to the last', () => {
      cancel.focus();
      const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true });
      spyOn(event, 'preventDefault');

      component.onTab(event);

      expect(event.preventDefault).toHaveBeenCalled();
      expect(document.activeElement).toBe(confirm);
    });

    it('leaves Tab alone in the middle of the dialog', () => {
      cancel.focus();
      const event = new KeyboardEvent('keydown', { key: 'Tab' });
      spyOn(event, 'preventDefault');

      component.onTab(event);

      expect(event.preventDefault).not.toHaveBeenCalled();
    });

    it('does not trap Tab while the dialog is closed', () => {
      component.open = false;
      const event = new KeyboardEvent('keydown', { key: 'Tab' });
      spyOn(event, 'preventDefault');

      component.onTab(event);

      expect(event.preventDefault).not.toHaveBeenCalled();
    });

    it('pulls focus back in when it has escaped the panel entirely', () => {
      opener.focus();
      const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true });
      spyOn(event, 'preventDefault');

      component.onTab(event);

      expect(event.preventDefault).toHaveBeenCalled();
      expect(document.activeElement).toBe(confirm);
    });
  });
});
