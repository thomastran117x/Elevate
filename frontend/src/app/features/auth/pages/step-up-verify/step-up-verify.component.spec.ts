import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { installMemoryStorage } from '@testing';

import { StepUpVerifyComponent } from './step-up-verify.component';
import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';
import { AuthenticatedSessionResponse } from '../../../../core/models/auth-response.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import {
  AuthService,
  LoginStepUpChallengeResponse,
  LoginStepUpMethod,
  PendingLoginStepUpStorageKey,
  StartLoginStepUpResponse,
} from '../../services/auth.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';

type AuthSpy = jasmine.SpyObj<
  Pick<
    AuthService,
    | 'startLoginStepUp'
    | 'verifyLoginStepUp'
    | 'verifyTotpLoginStepUp'
    | 'verifyRecoveryCodeLoginStepUp'
  >
>;

const RECOVERY_CODE = 'ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-2345';

const session: AuthenticatedSessionResponse = {
  AccessToken: 'token',
  ExpiresAtUtc: '2026-10-31T15:31:00Z',
  ReturnPath: '/events/4',
};

function pendingChallenge(
  methods: LoginStepUpMethod[] = ['totp', 'recovery_code', 'email'],
): LoginStepUpChallengeResponse {
  return {
    Challenge: 'challenge-1',
    ExpiresAtUtc: '2026-10-31T15:30:00Z',
    AvailableMethods: methods,
    MaskedPhone: null,
    MaskedEmail: 'm***@example.com',
  };
}

function started(method: LoginStepUpMethod): StartLoginStepUpResponse {
  return {
    Challenge: 'challenge-2',
    ExpiresAtUtc: '2026-10-31T15:30:00Z',
    SelectedMethod: method,
    MaskedDestination: method === 'recovery_code' ? 'recovery code' : 'authenticator app',
    CooldownEndsAtUtc: '0001-01-01T00:00:00',
    AvailableMethods: ['totp', 'recovery_code', 'email'],
    MaskedPhone: null,
    MaskedEmail: 'm***@example.com',
  };
}

describe('StepUpVerifyComponent', () => {
  let fixture: ComponentFixture<StepUpVerifyComponent>;
  let component: StepUpVerifyComponent;
  let auth: AuthSpy;
  let bootstrapSession: jasmine.Spy;
  let restoreStorage: () => void;

  async function create(seed: Record<string, string>): Promise<void> {
    restoreStorage = installMemoryStorage('session', seed);
    await TestBed.configureTestingModule({
      imports: [StepUpVerifyComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: SessionManagerService, useValue: { bootstrapSession } },
        { provide: AuthReturnUrlService, useValue: { consume: (fallback: string) => fallback } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StepUpVerifyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthSpy>('AuthService', [
      'startLoginStepUp',
      'verifyLoginStepUp',
      'verifyTotpLoginStepUp',
      'verifyRecoveryCodeLoginStepUp',
    ]);
    auth.startLoginStepUp.and.callFake((_challenge, method) => of(started(method)));
    auth.verifyRecoveryCodeLoginStepUp.and.returnValue(of(session));
    auth.verifyTotpLoginStepUp.and.returnValue(of(session));
    bootstrapSession = jasmine.createSpy('bootstrapSession').and.resolveTo();
  });

  afterEach(() => restoreStorage());

  it('reports a missing sign-in challenge', async () => {
    await create({});

    expect(component.status()).toBe('error');
    expect(component.message()).toContain('missing');
  });

  it('offers a recovery code only when the challenge allows one', async () => {
    await create({
      [PendingLoginStepUpStorageKey]: JSON.stringify(pendingChallenge(['totp', 'email'])),
    });
    expect(fixture.nativeElement.textContent).not.toContain('Use a recovery code');

    TestBed.resetTestingModule();
    restoreStorage();
    await create({ [PendingLoginStepUpStorageKey]: JSON.stringify(pendingChallenge()) });
    expect(fixture.nativeElement.textContent).toContain('Use a recovery code');
  });

  describe('with a recovery code', () => {
    beforeEach(async () => {
      await create({ [PendingLoginStepUpStorageKey]: JSON.stringify(pendingChallenge()) });
      component.selectMethod('recovery_code');
      fixture.detectChanges();
    });

    it('asks for a recovery code with a labelled, described field', () => {
      expect(auth.startLoginStepUp).toHaveBeenCalledOnceWith('challenge-1', 'recovery_code');
      expect(component.isRecoveryCodeActive).toBeTrue();
      expect(component.message()).toContain('saved recovery codes');
      const input = fixture.nativeElement.querySelector('#step-up-code') as HTMLInputElement;
      expect(input.getAttribute('aria-describedby')).toBe('step-up-code-hint');
      expect(input.getAttribute('autocomplete')).toBe('off');
      expect(
        fixture.nativeElement.querySelector('label[for="step-up-code"]').textContent,
      ).toContain('Recovery code');
      expect(JSON.parse(sessionStorage.getItem(PendingLoginStepUpStorageKey)!).Challenge).toBe(
        'challenge-2',
      );
    });

    it('refuses a six-digit code before asking the server', () => {
      component.codeForm.setValue({ code: '123456' });
      component.verifyCode();
      fixture.detectChanges();

      expect(auth.verifyRecoveryCodeLoginStepUp).not.toHaveBeenCalled();
      expect(fixture.nativeElement.textContent).toContain('Enter a recovery code in the format');
    });

    it('finishes the sign-in with the code and redirects', fakeAsync(() => {
      const router = TestBed.inject(Router);
      const navigate = spyOn(router, 'navigateByUrl').and.resolveTo(true);
      component.codeForm.setValue({ code: RECOVERY_CODE });

      component.verifyCode();
      tick();

      expect(auth.verifyRecoveryCodeLoginStepUp).toHaveBeenCalledOnceWith(
        'challenge-2',
        RECOVERY_CODE,
      );
      expect(bootstrapSession).toHaveBeenCalledOnceWith(session);
      expect(sessionStorage.getItem(PendingLoginStepUpStorageKey)).toBeNull();
      expect(component.status()).toBe('success');

      tick(800);
      expect(navigate).toHaveBeenCalledWith('/events/4');
    }));

    it('keeps the user on the code step when the code is refused', () => {
      auth.verifyRecoveryCodeLoginStepUp.and.returnValue(
        throwError(
          () =>
            new ApiClientClientError('Invalid or already used recovery code.', 401, 'UNAUTHORIZED'),
        ),
      );
      component.codeForm.setValue({ code: RECOVERY_CODE });

      component.verifyCode();

      expect(component.error).toBe('Invalid or already used recovery code.');
      expect(component.message()).toContain('saved recovery codes');
      expect(component.isRecoveryCodeActive).toBeTrue();
    });

    it('switches back to six-digit codes for the authenticator', () => {
      component.restart();
      component.selectMethod('totp');
      component.codeForm.setValue({ code: '123456' });

      component.verifyCode();

      expect(auth.verifyTotpLoginStepUp).toHaveBeenCalledOnceWith('challenge-2', '123456');
      expect(auth.verifyRecoveryCodeLoginStepUp).not.toHaveBeenCalled();
    });
  });
});
