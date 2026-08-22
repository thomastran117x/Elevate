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
});
