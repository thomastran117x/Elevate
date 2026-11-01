import { PLATFORM_ID } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { flushPromises } from '@testing';

import {
  RECOVERY_CODES_FILE_NAME,
  RecoveryCodesCardComponent,
} from './recovery-codes-card.component';
import {
  AuthService,
  RecoveryCodeStatusResponse,
  RecoveryCodesResponse,
} from '../../../../../../auth/services/auth.service';
import { ApiClientClientError } from '../../../../../../../core/api/models/api-client-error.model';

type AuthSpy = jasmine.SpyObj<
  Pick<AuthService, 'getRecoveryCodeStatus' | 'regenerateRecoveryCodes'>
>;

const CODES = Array.from(
  { length: 10 },
  (_, index) => `ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23${index.toString().padStart(2, '0')}`,
);

function status(overrides: Partial<RecoveryCodeStatusResponse> = {}): RecoveryCodeStatusResponse {
  return {
    remainingCount: 10,
    totalCount: 10,
    generatedAtUtc: '2026-10-31T12:00:00Z',
    ...overrides,
  };
}

const generated: RecoveryCodesResponse = { codes: CODES, generatedAtUtc: '2026-10-31T13:00:00Z' };

describe('RecoveryCodesCardComponent', () => {
  let fixture: ComponentFixture<RecoveryCodesCardComponent>;
  let component: RecoveryCodesCardComponent;
  let auth: AuthSpy;
  let stepUpRequired: jasmine.Spy;

  async function create(platformId = 'browser'): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [RecoveryCodesCardComponent],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: PLATFORM_ID, useValue: platformId },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RecoveryCodesCardComponent);
    component = fixture.componentInstance;
    stepUpRequired = jasmine.createSpy('stepUpRequired');
    component.stepUpRequired.subscribe(stepUpRequired);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function button(label: string): HTMLButtonElement {
    const match = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((candidate) => candidate.textContent?.trim() === label);
    expect(match).withContext(`button "${label}"`).toBeDefined();
    return match!;
  }

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthSpy>('AuthService', [
      'getRecoveryCodeStatus',
      'regenerateRecoveryCodes',
    ]);
    auth.getRecoveryCodeStatus.and.returnValue(of(status()));
    auth.regenerateRecoveryCodes.and.returnValue(of(generated));
  });

  describe('status', () => {
    it('shows a loading line until the status arrives', async () => {
      const pending = new Subject<RecoveryCodeStatusResponse>();
      auth.getRecoveryCodeStatus.and.returnValue(pending);
      await create();

      expect(text()).toContain('Loading recovery code status');

      pending.next(status());
      pending.complete();
      fixture.detectChanges();

      expect(text()).not.toContain('Loading recovery code status');
    });

    it('invites a first set when none exists', async () => {
      auth.getRecoveryCodeStatus.and.returnValue(
        of(status({ remainingCount: 0, generatedAtUtc: null })),
      );
      await create();

      expect(component.level()).toBe('none');
      expect(text()).toContain("You haven't generated recovery codes yet.");
      expect(button('Generate codes')).toBeTruthy();
    });

    it('reports the remaining count without a warning while enough remain', async () => {
      await create();

      expect(component.level()).toBe('ok');
      const line = fixture.nativeElement.querySelector('[data-testid="recovery-codes-status"]');
      expect(line.textContent).toContain('10 of 10');
      expect(line.getAttribute('role')).toBeNull();
    });

    it('warns politely when few codes remain', async () => {
      auth.getRecoveryCodeStatus.and.returnValue(of(status({ remainingCount: 3 })));
      await create();

      const line = fixture.nativeElement.querySelector('[data-testid="recovery-codes-status"]');
      expect(component.level()).toBe('low');
      expect(line.getAttribute('role')).toBe('status');
      expect(line.textContent).toContain('Only 3 of 10 recovery codes left');
    });

    it('alerts when every code is used', async () => {
      auth.getRecoveryCodeStatus.and.returnValue(of(status({ remainingCount: 0 })));
      await create();

      const line = fixture.nativeElement.querySelector('[data-testid="recovery-codes-status"]');
      expect(component.level()).toBe('empty');
      expect(line.getAttribute('role')).toBe('alert');
    });

    it('reports a status that cannot be loaded', async () => {
      auth.getRecoveryCodeStatus.and.returnValue(
        throwError(() => new ApiClientClientError('Service unavailable', 503, 'UNAVAILABLE')),
      );
      await create();

      expect(component.error()).toBe('Service unavailable');
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        'Service unavailable',
      );
    });
  });

  describe('generation', () => {
    it('generates a first set straight away and shows it once, focused', async () => {
      auth.getRecoveryCodeStatus.and.returnValue(
        of(status({ remainingCount: 0, generatedAtUtc: null })),
      );
      await create();

      button('Generate codes').click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(auth.regenerateRecoveryCodes).toHaveBeenCalledTimes(1);
      const items = Array.from(fixture.nativeElement.querySelectorAll('ol li')).map((li) =>
        (li as HTMLElement).textContent?.trim(),
      );
      expect(items).toEqual(CODES);
      expect(text()).toContain("These codes won't be shown again.");
      const heading = fixture.nativeElement.querySelector('#recovery-codes-list-title');
      expect(document.activeElement).toBe(heading);
      expect(component.status()).toEqual({
        remainingCount: 10,
        totalCount: 10,
        generatedAtUtc: generated.generatedAtUtc,
      });
    });

    it('asks before replacing an existing set, and can be cancelled', async () => {
      await create();

      button('Generate new codes').click();
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('[role="alertdialog"]');
      expect(dialog.textContent).toContain('Your current recovery codes will stop working');
      expect(auth.regenerateRecoveryCodes).not.toHaveBeenCalled();

      button('Keep current codes').click();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeNull();
      expect(auth.regenerateRecoveryCodes).not.toHaveBeenCalled();
    });

    it('replaces the set once confirmed', async () => {
      await create();

      button('Generate new codes').click();
      fixture.detectChanges();
      button('Replace codes').click();
      fixture.detectChanges();

      expect(auth.regenerateRecoveryCodes).toHaveBeenCalledTimes(1);
      expect(component.codes()).toEqual(CODES);
      expect(component.confirming()).toBeFalse();
    });

    it('ignores a second request while one is in flight', async () => {
      const pending = new Subject<RecoveryCodesResponse>();
      auth.regenerateRecoveryCodes.and.returnValue(pending);
      await create();

      component.generate();
      component.generate();

      expect(auth.regenerateRecoveryCodes).toHaveBeenCalledTimes(1);
      expect(component.generating()).toBeTrue();
    });

    it('hands a missing step-up proof to the host instead of reporting it', async () => {
      auth.regenerateRecoveryCodes.and.returnValue(
        throwError(() => new ApiClientClientError('Verify again', 403, 'MFA_REQUIRED')),
      );
      await create();

      component.generate();

      expect(stepUpRequired).toHaveBeenCalledTimes(1);
      expect(component.error()).toBe('');
      expect(component.codes()).toBeNull();
    });

    it('reports any other failure', async () => {
      auth.regenerateRecoveryCodes.and.returnValue(
        throwError(() => new ApiClientClientError('Try later', 503, 'UNAVAILABLE')),
      );
      await create();

      component.generate();

      expect(component.error()).toBe('Try later');
      expect(stepUpRequired).not.toHaveBeenCalled();
    });
  });

  describe('saving the codes', () => {
    beforeEach(async () => {
      await create();
      component.generate();
      fixture.detectChanges();
    });

    it('copies every code, one per line', async () => {
      const writeText = jasmine.createSpy('writeText').and.resolveTo();
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      try {
        button('Copy all').click();
        await flushPromises();
        fixture.detectChanges();

        expect(writeText).toHaveBeenCalledOnceWith(CODES.join('\n'));
        expect(text()).toContain('Copied to your clipboard.');
      } finally {
        if (original) {
          Object.defineProperty(navigator, 'clipboard', original);
        } else {
          delete (navigator as unknown as Record<string, unknown>)['clipboard'];
        }
      }
    });

    it('points to the download when copying is unavailable or refused', async () => {
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
      try {
        component.copyAll();
        fixture.detectChanges();
        expect(text()).toContain('Download the file instead.');

        Object.defineProperty(navigator, 'clipboard', {
          value: { writeText: jasmine.createSpy('writeText').and.rejectWith(new Error('denied')) },
          configurable: true,
        });
        component.copyState.set('idle');
        component.copyAll();
        await flushPromises();

        expect(component.copyState()).toBe('failed');
      } finally {
        if (original) {
          Object.defineProperty(navigator, 'clipboard', original);
        } else {
          delete (navigator as unknown as Record<string, unknown>)['clipboard'];
        }
      }
    });

    it('downloads the codes as a text file', async () => {
      const createObjectURL = spyOn(URL, 'createObjectURL').and.returnValue('blob:codes');
      const revokeObjectURL = spyOn(URL, 'revokeObjectURL');
      const click = spyOn(HTMLAnchorElement.prototype, 'click');

      button('Download .txt').click();

      expect(click).toHaveBeenCalledTimes(1);
      const anchor = click.calls.mostRecent().object as HTMLAnchorElement;
      expect(anchor.download).toBe(RECOVERY_CODES_FILE_NAME);
      expect(anchor.href).toBe('blob:codes');
      const blob = createObjectURL.calls.mostRecent().args[0] as Blob;
      const content = await blob.text();
      CODES.forEach((code, index) => expect(content).toContain(`${index + 1}. ${code}`));
      expect(revokeObjectURL).toHaveBeenCalledOnceWith('blob:codes');
    });

    it('keeps the codes until the user confirms they saved them, then forgets them', () => {
      const done = button('Done');
      expect(done.disabled).toBeTrue();

      component.finish();
      expect(component.codes()).toEqual(CODES);

      const checkbox = fixture.nativeElement.querySelector(
        'input[type="checkbox"]',
      ) as HTMLInputElement;
      expect(checkbox.closest('label')?.textContent).toContain("I've saved these codes");
      checkbox.click();
      fixture.detectChanges();
      expect(done.disabled).toBeFalse();

      done.click();
      fixture.detectChanges();

      expect(component.codes()).toBeNull();
      expect(fixture.nativeElement.querySelector('ol')).toBeNull();
      expect(text()).toContain('Your recovery codes are ready.');
      expect(text()).toContain('10 of 10');
    });
  });

  it('never touches browser APIs while rendering on the server', async () => {
    await create('server');
    component.generate();
    const createObjectURL = spyOn(URL, 'createObjectURL');

    component.download();
    component.copyState.set('idle');

    expect(createObjectURL).not.toHaveBeenCalled();
  });
});
