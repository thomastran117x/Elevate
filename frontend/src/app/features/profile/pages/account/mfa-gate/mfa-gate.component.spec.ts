import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { flushPromises } from '@testing';

import { MfaGateComponent } from './mfa-gate.component';
import { AuthTokenService } from '../../../../../core/api/services/auth-token.service';
import {
  AuthService,
  SessionMfaMethod,
  SessionMfaOptionsResponse,
  SessionMfaStartResponse,
} from '../../../../auth/services/auth.service';
import { ApiClientClientError } from '../../../../../core/api/models/api-client-error.model';

type AuthSpy = jasmine.SpyObj<
  Pick<
    AuthService,
    'getSessionMfaStatus' | 'getSessionMfaOptions' | 'startSessionMfa' | 'verifySessionMfa'
  >
>;

function options(overrides: Partial<SessionMfaOptionsResponse> = {}): SessionMfaOptionsResponse {
  return {
    availableMethods: ['sms', 'totp'],
    maskedPhone: '+1 ***-**-1234',
    maskedEmail: 'm***@example.com',
    ...overrides,
  };
}

function started(overrides: Partial<SessionMfaStartResponse> = {}): SessionMfaStartResponse {
  return {
    selectedMethod: 'sms',
    maskedDestination: '+1 ***-**-1234',
    expiresAtUtc: '2026-09-01T00:05:00Z',
    cooldownEndsAtUtc: '2026-09-01T00:01:00Z',
    ...overrides,
  };
}

const gated = () => new ApiClientClientError('Verify first', 401, 'MFA_REQUIRED');

describe('MfaGateComponent', () => {
  let fixture: ComponentFixture<MfaGateComponent>;
  let component: MfaGateComponent;
  let auth: AuthSpy;
  let refreshAccessToken: jasmine.Spy<() => Promise<void>>;
  let verified: jasmine.Spy;

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthSpy>('AuthService', [
      'getSessionMfaStatus',
      'getSessionMfaOptions',
      'startSessionMfa',
      'verifySessionMfa',
    ]);
    auth.getSessionMfaStatus.and.returnValue(of(undefined as void));
    auth.getSessionMfaOptions.and.returnValue(of(options()));
    auth.startSessionMfa.and.returnValue(of(started()));
    auth.verifySessionMfa.and.returnValue(of(undefined as void));

    refreshAccessToken = jasmine.createSpy('refreshAccessToken').and.resolveTo();

    await TestBed.configureTestingModule({
      imports: [MfaGateComponent],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: AuthTokenService, useValue: { refreshAccessToken } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MfaGateComponent);
    component = fixture.componentInstance;
    verified = jasmine.createSpy('verified');
    component.verified.subscribe(verified);
  });

  describe('initial status probe', () => {
    it('emits immediately when the session is already verified', () => {
      fixture.detectChanges();

      expect(verified).toHaveBeenCalledTimes(1);
      expect(component.checking).toBeFalse();
      expect(refreshAccessToken).not.toHaveBeenCalled();
    });

    it('mints a fresh token once and re-checks when the session is gated', async () => {
      // A pre-sid-claim token cannot bind a verification, so one silent refresh is tried.
      auth.getSessionMfaStatus.and.returnValues(throwError(gated), of(undefined as void));

      fixture.detectChanges();
      await flushPromises();

      expect(refreshAccessToken).toHaveBeenCalledTimes(1);
      expect(auth.getSessionMfaStatus).toHaveBeenCalledTimes(2);
      expect(verified).toHaveBeenCalledTimes(1);
      expect(component.checking).toBeFalse();
    });

    it('stays locked without looping when the re-check is still gated', async () => {
      auth.getSessionMfaStatus.and.returnValue(throwError(gated));

      fixture.detectChanges();
      await flushPromises();

      expect(refreshAccessToken).toHaveBeenCalledTimes(1);
      expect(auth.getSessionMfaStatus).toHaveBeenCalledTimes(2);
      expect(verified).not.toHaveBeenCalled();
      expect(component.error).toBe('');
    });

    it('stops checking when the silent refresh itself fails', async () => {
      auth.getSessionMfaStatus.and.returnValue(throwError(gated));
      refreshAccessToken.and.returnValue(Promise.reject(new Error('refresh failed')));

      fixture.detectChanges();
      await flushPromises();

      expect(component.checking).toBeFalse();
      expect(verified).not.toHaveBeenCalled();
    });

    it('surfaces any non-gating failure as an error', () => {
      auth.getSessionMfaStatus.and.returnValue(
        throwError(() => new ApiClientClientError('Status unavailable.', 500, 'BOOM')),
      );

      fixture.detectChanges();

      expect(component.error).toBe('Status unavailable.');
      expect(refreshAccessToken).not.toHaveBeenCalled();
      expect(component.checking).toBeFalse();
    });
  });

  describe('modal', () => {
    beforeEach(() => {
      auth.getSessionMfaStatus.and.returnValue(throwError(gated));
      fixture.detectChanges();
    });

    it('opens on the method step and preselects the first available method', () => {
      component.openModal();

      expect(component.modalOpen).toBeTrue();
      expect(component.step).toBe('method');
      expect(component.options).toEqual(options());
      expect(component.method).toBe('sms');
      expect(component.optionsLoading).toBeFalse();
    });

    it('falls back to email when the account reports no methods', () => {
      auth.getSessionMfaOptions.and.returnValue(of(options({ availableMethods: [] })));

      component.openModal();

      expect(component.method).toBe('email');
    });

    it('reports a failure to load the options', () => {
      auth.getSessionMfaOptions.and.returnValue(
        throwError(() => new ApiClientClientError('No options.', 500, 'BOOM')),
      );

      component.openModal();

      expect(component.error).toBe('No options.');
      expect(component.optionsLoading).toBeFalse();
    });

    it('clears prior state on reopen', () => {
      component.openModal();
      component.step = 'code';
      component.maskedDestination = '+1 ***-**-1234';
      component.error = 'stale';
      component.codeForm.setValue({ code: '123456' });

      component.openModal();

      expect(component.step).toBe('method');
      expect(component.maskedDestination).toBe('');
      expect(component.error).toBe('');
      expect(component.codeForm.getRawValue().code).toBe('');
    });

    it('closes and resets', () => {
      component.openModal();
      component.step = 'code';
      component.error = 'stale';

      component.closeModal();

      expect(component.modalOpen).toBeFalse();
      expect(component.step).toBe('method');
      expect(component.error).toBe('');
    });

    it('refuses to close mid-flight', () => {
      const pending = new Subject<SessionMfaStartResponse>();
      auth.startSessionMfa.and.returnValue(pending.asObservable());
      component.openModal();
      component.selectMethod('sms');
      component.continueToCode();

      component.closeModal();
      expect(component.modalOpen).toBeTrue();

      pending.next(started());
      pending.complete();
      component.closeModal();
      expect(component.modalOpen).toBeFalse();
    });
  });

  describe('method selection', () => {
    beforeEach(() => {
      auth.getSessionMfaStatus.and.returnValue(throwError(gated));
      fixture.detectChanges();
      component.openModal();
    });

    it('selects a method and clears any error', () => {
      component.error = 'stale';

      component.selectMethod('totp');

      expect(component.method).toBe('totp');
      expect(component.error).toBe('');
    });

    it('treats SMS and email as needing delivery, and TOTP as not', () => {
      component.selectMethod('sms');
      expect(component.selectedNeedsDelivery).toBeTrue();

      component.selectMethod('email');
      expect(component.selectedNeedsDelivery).toBeTrue();

      component.selectMethod('totp');
      expect(component.selectedNeedsDelivery).toBeFalse();
    });

    it('goes straight to code entry for TOTP without sending anything', () => {
      component.selectMethod('totp');

      component.continueToCode();

      expect(auth.startSessionMfa).not.toHaveBeenCalled();
      expect(component.step).toBe('code');
    });

    it('sends a code first for SMS, then advances', () => {
      component.selectMethod('sms');

      component.continueToCode();

      expect(auth.startSessionMfa).toHaveBeenCalledOnceWith('sms');
      expect(component.maskedDestination).toBe('+1 ***-**-1234');
      expect(component.step).toBe('code');
      expect(component.sending).toBeFalse();
    });

    it('stays on the method step when sending fails', () => {
      auth.startSessionMfa.and.returnValue(
        throwError(() => new ApiClientClientError('SMS is down.', 503, 'UNAVAILABLE')),
      );
      component.selectMethod('sms');

      component.continueToCode();

      expect(component.error).toBe('SMS is down.');
      expect(component.step).toBe('method');
      expect(component.sending).toBeFalse();
    });

    it('does nothing when no method is chosen', () => {
      component.method = null;

      component.continueToCode();

      expect(auth.startSessionMfa).not.toHaveBeenCalled();
      expect(component.step).toBe('method');
    });

    it('resends without leaving the code step', () => {
      component.selectMethod('sms');
      component.continueToCode();
      component.codeForm.setValue({ code: '111111' });

      component.sendCode();

      expect(auth.startSessionMfa).toHaveBeenCalledTimes(2);
      expect(component.step).toBe('code');
      // A resend must not wipe what the user has already typed.
      expect(component.codeForm.getRawValue().code).toBe('111111');
    });

    it('never sends for a method that needs no delivery', () => {
      component.selectMethod('totp');

      component.sendCode();

      expect(auth.startSessionMfa).not.toHaveBeenCalled();
    });

    it('returns to the method step, discarding the typed code', () => {
      component.selectMethod('totp');
      component.continueToCode();
      component.codeForm.setValue({ code: '123456' });
      component.error = 'stale';

      component.backToMethodStep();

      expect(component.step).toBe('method');
      expect(component.error).toBe('');
      expect(component.codeForm.getRawValue().code).toBe('');
    });
  });

  describe('code verification', () => {
    beforeEach(() => {
      auth.getSessionMfaStatus.and.returnValue(throwError(gated));
      fixture.detectChanges();
      component.openModal();
      component.selectMethod('totp');
      component.continueToCode();
    });

    it('verifies, closes the modal and emits', () => {
      component.codeForm.setValue({ code: '123456' });

      component.verifyCode();

      expect(auth.verifySessionMfa).toHaveBeenCalledOnceWith('totp', '123456');
      expect(component.modalOpen).toBeFalse();
      expect(verified).toHaveBeenCalledTimes(1);
      expect(component.verifying).toBeFalse();
    });

    it('rejects a code that is not six digits', () => {
      component.codeForm.setValue({ code: '12ab' });

      component.verifyCode();

      expect(auth.verifySessionMfa).not.toHaveBeenCalled();
      expect(component.codeForm.touched).toBeTrue();
    });

    it('keeps the modal open when the code is wrong', () => {
      auth.verifySessionMfa.and.returnValue(
        throwError(() => new ApiClientClientError('That code is incorrect.', 400, 'INVALID_CODE')),
      );
      component.codeForm.setValue({ code: '000000' });

      component.verifyCode();

      expect(component.error).toBe('That code is incorrect.');
      expect(component.modalOpen).toBeTrue();
      expect(verified).not.toHaveBeenCalled();
      expect(component.verifying).toBeFalse();
    });

    it('does nothing when the method was cleared', () => {
      component.method = null;
      component.codeForm.setValue({ code: '123456' });

      component.verifyCode();

      expect(auth.verifySessionMfa).not.toHaveBeenCalled();
    });
  });

  describe('labels', () => {
    const cases: Array<[SessionMfaMethod, string, string]> = [
      ['totp', 'Authenticator app', 'Enter a 6-digit code from your authenticator app.'],
      ['sms', 'Text message (SMS)', 'We text a 6-digit code to your verified phone.'],
      ['email', 'Email', 'We email a 6-digit code to your inbox.'],
    ];

    for (const [method, label, description] of cases) {
      it(`describes ${method}`, () => {
        expect(component.methodLabel(method)).toBe(label);
        expect(component.methodDescription(method)).toBe(description);
      });
    }
  });
});
