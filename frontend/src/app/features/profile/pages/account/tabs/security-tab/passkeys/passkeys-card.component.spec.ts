import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NEVER, Observable, of, throwError } from 'rxjs';

import { flushPromises } from '@testing';

import { PasskeysCardComponent } from './passkeys-card.component';
import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import {
  Passkey,
  PasskeyList,
  PasskeyOutcome,
  PasskeysService,
} from '../../../../../../auth/services/passkeys.service';

const PASSKEY: Passkey = {
  id: 3,
  name: 'Work laptop',
  syncable: true,
  backedUp: true,
  canRemove: true,
  createdAt: '2026-11-01T09:00:00Z',
  lastUsedAt: '2026-11-05T10:00:00Z',
};

function list(passkeys: Passkey[] = [PASSKEY], limit = 10): PasskeyList {
  return { passkeys, limit };
}

describe('PasskeysCardComponent', () => {
  let fixture: ComponentFixture<PasskeysCardComponent>;
  let component: PasskeysCardComponent;
  let passkeys: jasmine.SpyObj<PasskeysService>;
  let stepUpRequired: jasmine.Spy;

  const refusal = (code: string, message = 'refused', status = 409) =>
    new ApiClientClientError(message, status, code);

  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [PasskeysCardComponent],
      providers: [{ provide: PasskeysService, useValue: passkeys }],
    }).compileComponents();

    fixture = TestBed.createComponent(PasskeysCardComponent);
    component = fixture.componentInstance;
    stepUpRequired = jasmine.createSpy('stepUpRequired');
    component.stepUpRequired.subscribe(stepUpRequired);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(label: string): HTMLButtonElement {
    const match = Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) =>
        candidate.textContent?.trim() === label || candidate.getAttribute('aria-label') === label,
    );
    expect(match).withContext(`button "${label}"`).toBeDefined();
    return match!;
  }

  function banner(): string {
    return el().querySelector('[data-testid="passkeys-banner"]')?.textContent?.trim() ?? '';
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushPromises();
    fixture.detectChanges();
  }

  beforeEach(() => {
    passkeys = jasmine.createSpyObj<PasskeysService>('PasskeysService', [
      'isSupported',
      'list',
      'register',
      'rename',
      'remove',
    ]);
    passkeys.isSupported.and.returnValue(true);
    passkeys.list.and.returnValue(of(list()));
  });

  it('lists each passkey by name, sync state, and use, without credential data', async () => {
    await create();

    const row = el().querySelector('[data-testid="passkey-3"]')!;
    expect(row.textContent).toContain('Work laptop');
    expect(row.textContent).toContain('Synced');
    expect(row.textContent).toContain('Last used');
    expect(el().querySelector('ul')?.getAttribute('aria-label')).toBe('Passkeys');
  });

  it('marks a device-bound, unused passkey, and one that is the last way to sign in', async () => {
    passkeys.list.and.returnValue(
      of(list([{ ...PASSKEY, syncable: false, lastUsedAt: null, canRemove: false }])),
    );
    await create();

    const row = el().querySelector('[data-testid="passkey-3"]')!;
    expect(row.textContent).toContain('This device only');
    expect(row.textContent).toContain('Not used yet.');
    expect(row.textContent).toContain('only way to sign in');
    expect(button('Remove Work laptop').disabled).toBeTrue();
  });

  it('shows an empty state', async () => {
    passkeys.list.and.returnValue(of(list([])));
    await create();

    expect(el().textContent).toContain('No passkeys yet.');
  });

  it('reports a list that will not load', async () => {
    passkeys.list.and.returnValue(throwError(() => new Error('Network down')));
    await create();

    expect(banner()).toContain('Network down');
    // Loading on arrival is not the user's action, so the message does not take focus.
    expect(document.activeElement).not.toBe(el().querySelector('[data-testid="passkeys-banner"]'));
  });

  it('explains an unsupported browser and offers no way to add one', async () => {
    passkeys.isSupported.and.returnValue(false);
    await create();

    expect(el().querySelector('[data-testid="passkeys-unsupported"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="add-passkey"]')).toBeNull();
    component.add();
    expect(passkeys.register).not.toHaveBeenCalled();
  });

  describe('adding', () => {
    it('registers with the given name, confirms, and reloads the list', async () => {
      passkeys.register.and.returnValue(
        of({ status: 'done', value: { ...PASSKEY, name: 'Phone' } }),
      );
      await create();
      const input = el().querySelector('#new-passkey-name') as HTMLInputElement;
      input.value = '  Phone  ';
      input.dispatchEvent(new Event('input'));

      button('Add a passkey').click();
      await settle();

      expect(passkeys.register).toHaveBeenCalledWith('Phone');
      expect(banner()).toContain('Phone was added');
      expect(passkeys.list).toHaveBeenCalledTimes(2);
      expect(component.newName()).toBe('');
    });

    it('sends no name when none was typed', async () => {
      passkeys.register.and.returnValue(
        of({ status: 'cancelled' }) as Observable<PasskeyOutcome<Passkey>>,
      );
      await create();

      component.add();

      expect(passkeys.register).toHaveBeenCalledWith(null);
    });

    const outcomes: Array<[PasskeyOutcome<Passkey>['status'], string]> = [
      ['cancelled', 'was cancelled'],
      ['already_registered', 'already has a passkey'],
      ['unsupported', "can't create passkeys"],
      ['failed', 'Adding a passkey failed'],
    ];
    for (const [status, message] of outcomes) {
      it(`explains a ${status} prompt`, async () => {
        passkeys.register.and.returnValue(of({ status }) as Observable<PasskeyOutcome<Passkey>>);
        await create();

        component.add();
        await settle();

        expect(banner()).toContain(message);
        expect(el().querySelector('[data-testid="passkeys-banner"]')?.getAttribute('role')).toBe(
          'alert',
        );
        expect(passkeys.list).toHaveBeenCalledTimes(1);
      });
    }

    it('hands a missing step-up to the tab', async () => {
      passkeys.register.and.returnValue(
        throwError(() => refusal(MFA_REQUIRED_ERROR_CODE, 'x', 403)),
      );
      await create();

      component.add();

      expect(stepUpRequired).toHaveBeenCalled();
    });

    it("shows the server's reason, such as the limit", async () => {
      passkeys.register.and.returnValue(
        throwError(() =>
          refusal(
            'PASSKEY_LIMIT_REACHED',
            'You have added as many passkeys as an account can have.',
          ),
        ),
      );
      await create();

      component.add();
      await settle();

      expect(banner()).toContain('as many passkeys');
    });

    it('ignores a second add while one is in progress, and stops at the limit', async () => {
      passkeys.register.and.returnValue(NEVER);
      await create();

      component.add();
      component.add();
      expect(passkeys.register).toHaveBeenCalledTimes(1);
      fixture.detectChanges();
      expect(button('Waiting for your device...').disabled).toBeTrue();

      passkeys.list.and.returnValue(of(list([PASSKEY], 1)));
      fixture.destroy();
      TestBed.resetTestingModule();
      await create();
      expect(
        (el().querySelector('[data-testid="add-passkey"]') as HTMLButtonElement).disabled,
      ).toBeTrue();
      expect(el().textContent).toContain('the most an account can have');
    });
  });

  describe('renaming', () => {
    beforeEach(async () => {
      await create();
      button('Rename Work laptop').click();
      await settle();
    });

    function nameInput(): HTMLInputElement {
      return el().querySelector('#passkey-name-3') as HTMLInputElement;
    }

    function type(value: string): void {
      nameInput().value = value;
      nameInput().dispatchEvent(new Event('input'));
    }

    it('edits inline with a labelled, focused field', () => {
      expect(el().querySelector('label[for="passkey-name-3"]')?.textContent).toContain(
        'Passkey name',
      );
      expect(nameInput().value).toBe('Work laptop');
      expect(document.activeElement).toBe(nameInput());
    });

    it('saves the new name and confirms it', async () => {
      passkeys.rename.and.returnValue(of({ ...PASSKEY, name: 'Phone' }));
      type(' Phone ');

      button('Save').click();
      await settle();

      expect(passkeys.rename).toHaveBeenCalledWith(3, 'Phone');
      expect(el().querySelector('[data-testid="passkey-3"]')?.textContent).toContain('Phone');
      expect(banner()).toContain('Renamed to Phone.');
      expect(nameInput()).toBeNull();
    });

    it('refuses a blank name without asking the server', async () => {
      type('   ');
      button('Save').click();
      await settle();

      expect(passkeys.rename).not.toHaveBeenCalled();
      expect(el().querySelector('#passkey-name-error-3')?.textContent).toContain(
        'Give the passkey a name.',
      );
      expect(nameInput().getAttribute('aria-invalid')).toBe('true');
    });

    it('closes without a request when the name is unchanged', async () => {
      button('Save').click();
      await settle();

      expect(passkeys.rename).not.toHaveBeenCalled();
      expect(nameInput()).toBeNull();
    });

    it('cancels with Escape and returns focus to the rename button', async () => {
      nameInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await settle();

      expect(nameInput()).toBeNull();
      expect(document.activeElement?.id).toBe('passkey-rename-3');
    });

    it('cancels with the Cancel button', async () => {
      button('Cancel').click();
      await settle();

      expect(nameInput()).toBeNull();
    });

    it('shows a refusal under the field, or hands a missing step-up to the tab', async () => {
      type('Phone');
      passkeys.rename.and.returnValue(
        throwError(() => refusal('NOT_FOUND', 'Passkey not found.', 404)),
      );
      button('Save').click();
      await settle();

      expect(el().querySelector('#passkey-name-error-3')?.textContent).toContain(
        'Passkey not found.',
      );

      passkeys.rename.and.returnValue(throwError(() => refusal(MFA_REQUIRED_ERROR_CODE, 'x', 403)));
      button('Save').click();
      await settle();

      expect(stepUpRequired).toHaveBeenCalled();
    });
  });

  describe('removing', () => {
    beforeEach(async () => {
      await create();
    });

    it('asks first, then removes and reloads', async () => {
      passkeys.remove.and.returnValue(of(undefined));
      button('Remove Work laptop').click();
      await settle();

      expect(component.confirmingRemove()).toEqual(PASSKEY);
      expect(el().textContent).toContain('Remove Work laptop?');

      component.onRemoveResolved(true);
      await settle();

      expect(passkeys.remove).toHaveBeenCalledWith(3);
      expect(banner()).toContain('Work laptop was removed');
      expect(passkeys.list).toHaveBeenCalledTimes(2);
      expect(component.confirmingRemove()).toBeNull();
    });

    it('keeps the passkey when the dialog is dismissed', () => {
      component.requestRemove(PASSKEY);
      component.onRemoveResolved(false);

      expect(passkeys.remove).not.toHaveBeenCalled();
      expect(component.confirmingRemove()).toBeNull();
    });

    it("explains the last-sign-in-method refusal in the server's words", async () => {
      passkeys.remove.and.returnValue(
        throwError(() =>
          refusal('LAST_SIGN_IN_METHOD', 'This passkey is your only way to sign in.'),
        ),
      );
      component.requestRemove(PASSKEY);
      component.onRemoveResolved(true);
      await settle();

      expect(banner()).toContain('only way to sign in');
    });

    it('hands a missing step-up to the tab', () => {
      passkeys.remove.and.returnValue(throwError(() => refusal(MFA_REQUIRED_ERROR_CODE, 'x', 403)));
      component.requestRemove(PASSKEY);
      component.onRemoveResolved(true);

      expect(stepUpRequired).toHaveBeenCalled();
    });
  });
});
