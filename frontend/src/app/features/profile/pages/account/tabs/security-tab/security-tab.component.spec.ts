import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { provideHttpTesting, provideTestStore } from '@testing';

import { SecurityTabComponent } from './security-tab.component';
import {
  AuthService,
  MfaChallengeResponse,
  MfaSettingsResponse,
  TotpEnrollmentStartResponse,
} from '../../../../../auth/services/auth.service';
import { ApiClientClientError } from '../../../../../../core/api/models/api-client-error.model';

function makeSettings(overrides: Partial<MfaSettingsResponse> = {}): MfaSettingsResponse {
  return {
    email: { maskedEmail: 'm***@example.com', isEnabled: true },
    sms: {
      enrollmentAvailable: true,
      isConfigured: false,
      isEnabled: false,
      maskedPhoneNumber: null,
      phoneVerifiedAtUtc: null,
      canEnroll: true,
      canEnable: false,
      canDisable: false,
      canRemove: false,
    },
    totp: {
      enrollmentAvailable: true,
      isConfigured: false,
      isEnabled: false,
      enrolledAtUtc: null,
      disabledAtUtc: null,
      canEnroll: true,
      canEnable: false,
      canDisable: false,
      canRemove: false,
    },
    ...overrides,
  };
}

const challenge: MfaChallengeResponse = {
  Challenge: 'chal-1',
  ExpiresAtUtc: '2026-09-01T00:05:00Z',
  Channel: 'sms',
  MaskedDestination: '+1 ***-**-1234',
};

const enrollment: TotpEnrollmentStartResponse = {
  SecretKey: 'SECRET',
  QrCodeUri: 'otpauth://totp/Event:member',
  ExpiresAtUtc: '2026-09-01T00:05:00Z',
};

type AuthSpy = jasmine.SpyObj<
  Pick<
    AuthService,
    | 'getMfaStatus'
    | 'startMfaEnrollment'
    | 'startMfaEnable'
    | 'verifyMfaEnrollment'
    | 'disableMfa'
    | 'removeMfa'
    | 'startTotpEnrollment'
    | 'verifyTotpEnrollment'
    | 'enableTotp'
    | 'disableTotp'
    | 'removeTotp'
    | 'getSessionMfaStatus'
  >
>;

describe('SecurityTabComponent', () => {
  let component: SecurityTabComponent;
  let fixture: ComponentFixture<SecurityTabComponent>;
  let auth: AuthSpy;

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthSpy>('AuthService', [
      'getMfaStatus',
      'startMfaEnrollment',
      'startMfaEnable',
      'verifyMfaEnrollment',
      'disableMfa',
      'removeMfa',
      'startTotpEnrollment',
      'verifyTotpEnrollment',
      'enableTotp',
      'disableTotp',
      'removeTotp',
      'getSessionMfaStatus',
    ]);
    auth.getMfaStatus.and.returnValue(of(makeSettings()));
    // The gate starts unverified; specs call onMfaVerified() to stand in for its emission.
    auth.getSessionMfaStatus.and.returnValue(
      throwError(() => new ApiClientClientError('Verify first', 401, 'MFA_REQUIRED')),
    );

    await TestBed.configureTestingModule({
      imports: [SecurityTabComponent],
      // The nested <app-mfa-gate> pulls in AuthTokenService, hence the store and
      // HTTP backend even though this spec drives the component directly.
      providers: [
        { provide: AuthService, useValue: auth },
        ...provideHttpTesting(),
        ...provideTestStore(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SecurityTabComponent);
    component = fixture.componentInstance;
  });

  /** The MFA gate drives the initial load; specs stand in for its (verified) emission. */
  function verifyGate(): void {
    component.onMfaVerified();
  }

  describe('initial load', () => {
    it('loads nothing until the MFA gate reports a verified session', () => {
      fixture.detectChanges();

      expect(auth.getMfaStatus).not.toHaveBeenCalled();
      expect(component.mfaVerified).toBeFalse();
      expect(component.loading).toBeTrue();
    });

    it('fetches settings once the gate emits', () => {
      verifyGate();

      expect(auth.getMfaStatus).toHaveBeenCalledTimes(1);
      expect(component.mfaVerified).toBeTrue();
      expect(component.loading).toBeFalse();
      expect(component.smsSettings?.canEnroll).toBeTrue();
    });

    it('falls back to the gate when the session verification has lapsed', () => {
      auth.getMfaStatus.and.returnValue(
        throwError(() => new ApiClientClientError('Re-verify', 401, 'MFA_REQUIRED')),
      );

      verifyGate();

      expect(component.mfaVerified).toBeFalse();
      expect(component.error).toBe('');
    });

    it('surfaces any other load failure as an error message', () => {
      auth.getMfaStatus.and.returnValue(
        throwError(() => new ApiClientClientError('Nope', 500, 'BOOM')),
      );

      verifyGate();

      expect(component.mfaVerified).toBeTrue();
      expect(component.error).toBe('Nope');
    });
  });

  describe('SMS enrollment', () => {
    beforeEach(() => verifyGate());

    it('does not call the API when the phone number is missing', () => {
      component.openSmsEditor();
      component.startSmsEnrollment();

      expect(auth.startMfaEnrollment).not.toHaveBeenCalled();
      expect(component.phoneForm.touched).toBeTrue();
    });

    it('starts the challenge and moves to the verification step', () => {
      auth.startMfaEnrollment.and.returnValue(of(challenge));

      component.openSmsEditor();
      component.phoneForm.setValue({ phoneNumber: '+15551234' });
      component.startSmsEnrollment();

      expect(auth.startMfaEnrollment).toHaveBeenCalledOnceWith('+15551234');
      expect(component.isSmsVerificationStep).toBeTrue();
      expect(component.smsFlow).toBe('enroll');
      expect(component.success).toContain('+1 ***-**-1234');
      expect(component.smsSubmitting).toBeFalse();
    });

    it('reports a failed start without entering the verification step', () => {
      auth.startMfaEnrollment.and.returnValue(
        throwError(() => new ApiClientClientError('Bad number.', 400, 'VALIDATION_FAILED')),
      );

      component.openSmsEditor();
      component.phoneForm.setValue({ phoneNumber: '+1' });
      component.startSmsEnrollment();

      expect(component.error).toBe('Bad number.');
      expect(component.isSmsVerificationStep).toBeFalse();
      expect(component.smsSubmitting).toBeFalse();
    });

    it('starts the re-enable flow without a phone number', () => {
      auth.startMfaEnable.and.returnValue(of(challenge));

      component.startSmsEnable();

      expect(component.smsFlow).toBe('enable');
      expect(component.isSmsVerificationStep).toBeTrue();
    });

    it('refuses to verify before a challenge exists', () => {
      component.smsCodeForm.setValue({ code: '123456' });
      component.verifySmsChallenge();

      expect(auth.verifyMfaEnrollment).not.toHaveBeenCalled();
      expect(component.error).toBe('Start SMS setup before verifying a code.');
    });

    it('rejects a code that is not six digits', () => {
      auth.startMfaEnrollment.and.returnValue(of(challenge));
      component.phoneForm.setValue({ phoneNumber: '+15551234' });
      component.startSmsEnrollment();

      component.smsCodeForm.setValue({ code: '12ab' });
      component.verifySmsChallenge();

      expect(auth.verifyMfaEnrollment).not.toHaveBeenCalled();
      expect(component.smsCodeForm.touched).toBeTrue();
    });

    it('completes enrollment and clears the flow', () => {
      auth.startMfaEnrollment.and.returnValue(of(challenge));
      auth.verifyMfaEnrollment.and.returnValue(
        of(makeSettings({ sms: { ...makeSettings().sms, isEnabled: true, isConfigured: true } })),
      );

      component.phoneForm.setValue({ phoneNumber: '+15551234' });
      component.startSmsEnrollment();
      component.smsCodeForm.setValue({ code: '123456' });
      component.verifySmsChallenge();

      expect(auth.verifyMfaEnrollment).toHaveBeenCalledOnceWith('123456', 'chal-1');
      expect(component.smsSettings?.isEnabled).toBeTrue();
      expect(component.isSmsVerificationStep).toBeFalse();
      expect(component.smsFlow).toBeNull();
      expect(component.smsEditorOpen).toBeFalse();
      expect(component.success).toBe('SMS MFA is now enabled.');
    });

    it('reports re-enabling separately from first enrollment', () => {
      auth.startMfaEnable.and.returnValue(of(challenge));
      auth.verifyMfaEnrollment.and.returnValue(of(makeSettings()));

      component.startSmsEnable();
      component.smsCodeForm.setValue({ code: '123456' });
      component.verifySmsChallenge();

      expect(component.success).toBe('SMS MFA has been re-enabled.');
    });

    it('keeps the verification step open and refreshes status when the code is wrong', () => {
      auth.startMfaEnrollment.and.returnValue(of(challenge));
      auth.verifyMfaEnrollment.and.returnValue(
        throwError(() => new ApiClientClientError('That code is incorrect.', 400, 'INVALID_CODE')),
      );

      component.phoneForm.setValue({ phoneNumber: '+15551234' });
      component.startSmsEnrollment();
      auth.getMfaStatus.calls.reset();

      component.smsCodeForm.setValue({ code: '000000' });
      component.verifySmsChallenge();

      expect(component.error).toBe('That code is incorrect.');
      expect(component.isSmsVerificationStep).toBeTrue();
      // Silent refresh keeps the error visible and does not flip loading back on.
      expect(auth.getMfaStatus).toHaveBeenCalledTimes(1);
      expect(component.loading).toBeFalse();
    });

    it('abandons the flow on cancel', () => {
      auth.startMfaEnrollment.and.returnValue(of(challenge));
      component.phoneForm.setValue({ phoneNumber: '+15551234' });
      component.startSmsEnrollment();

      component.cancelSmsChallenge();

      expect(component.isSmsVerificationStep).toBeFalse();
      expect(component.smsFlow).toBeNull();
      expect(component.success).toBe('');
      expect(component.phoneForm.getRawValue().phoneNumber).toBe('');
    });

    it('closes and resets the phone editor on cancel', () => {
      component.openSmsEditor();
      component.phoneForm.setValue({ phoneNumber: '+15551234' });

      component.cancelSmsEditor();

      expect(component.smsEditorOpen).toBeFalse();
      expect(component.phoneForm.getRawValue().phoneNumber).toBe('');
    });
  });

  describe('SMS disable and remove', () => {
    beforeEach(() => verifyGate());

    it('disables SMS and reports it', () => {
      auth.disableMfa.and.returnValue(of(makeSettings()));

      component.disableSms();

      expect(component.success).toBe('SMS MFA has been disabled.');
      expect(component.smsMutating).toBeFalse();
    });

    it('removes SMS and clears any in-flight flow', () => {
      auth.startMfaEnable.and.returnValue(of(challenge));
      auth.removeMfa.and.returnValue(of(makeSettings()));

      component.startSmsEnable();
      component.removeSms();

      expect(component.success).toBe('SMS MFA has been removed.');
      expect(component.isSmsVerificationStep).toBeFalse();
    });

    it('reports a failure without corrupting state', () => {
      auth.disableMfa.and.returnValue(
        throwError(() => new ApiClientClientError('Not allowed.', 403, 'FORBIDDEN')),
      );

      component.disableSms();

      expect(component.error).toBe('Not allowed.');
      expect(component.success).toBe('');
      expect(component.smsMutating).toBeFalse();
    });
  });

  describe('TOTP enrollment', () => {
    beforeEach(() => verifyGate());

    it('starts enrollment and exposes the setup payload', () => {
      auth.startTotpEnrollment.and.returnValue(of(enrollment));

      component.startTotpEnrollment();

      expect(component.isTotpSetupStep).toBeTrue();
      expect(component.totpEnrollment?.QrCodeUri).toBe('otpauth://totp/Event:member');
      expect(component.totpStarting).toBeFalse();
    });

    it('refuses to verify before enrollment has started', () => {
      component.totpSetupForm.setValue({ code: '123456' });
      component.verifyTotpEnrollment();

      expect(auth.verifyTotpEnrollment).not.toHaveBeenCalled();
      expect(component.error).toBe('Start TOTP setup before verifying a code.');
    });

    it('completes enrollment and closes the setup step', () => {
      auth.startTotpEnrollment.and.returnValue(of(enrollment));
      auth.verifyTotpEnrollment.and.returnValue(
        of(makeSettings({ totp: { ...makeSettings().totp, isEnabled: true } })),
      );

      component.startTotpEnrollment();
      component.totpSetupForm.setValue({ code: '123456' });
      component.verifyTotpEnrollment();

      expect(auth.verifyTotpEnrollment).toHaveBeenCalledOnceWith('123456');
      expect(component.isTotpSetupStep).toBeFalse();
      expect(component.totpSettings?.isEnabled).toBeTrue();
      expect(component.success).toBe('TOTP MFA is now enabled.');
    });

    it('keeps the setup step open when the code is rejected', () => {
      auth.startTotpEnrollment.and.returnValue(of(enrollment));
      auth.verifyTotpEnrollment.and.returnValue(
        throwError(() => new ApiClientClientError('Wrong code.', 400, 'INVALID_CODE')),
      );

      component.startTotpEnrollment();
      component.totpSetupForm.setValue({ code: '000000' });
      component.verifyTotpEnrollment();

      expect(component.error).toBe('Wrong code.');
      expect(component.isTotpSetupStep).toBeTrue();
    });

    it('discards the enrollment on cancel', () => {
      auth.startTotpEnrollment.and.returnValue(of(enrollment));

      component.startTotpEnrollment();
      component.cancelTotpEnrollment();

      expect(component.isTotpSetupStep).toBeFalse();
      expect(component.totpSetupForm.getRawValue().code).toBe('');
    });
  });

  describe('TOTP manage actions', () => {
    beforeEach(() => verifyGate());

    it('opens a confirmation step with an empty code', () => {
      component.totpManageForm.setValue({ code: '111111' });

      component.beginTotpAction('disable');

      expect(component.isTotpManageStep).toBeTrue();
      expect(component.totpManageAction).toBe('disable');
      expect(component.totpManageForm.getRawValue().code).toBe('');
    });

    it('does nothing when submitted with no action pending', () => {
      component.submitTotpAction();

      expect(auth.enableTotp).not.toHaveBeenCalled();
      expect(auth.disableTotp).not.toHaveBeenCalled();
      expect(auth.removeTotp).not.toHaveBeenCalled();
    });

    it('rejects a malformed confirmation code', () => {
      component.beginTotpAction('remove');
      component.totpManageForm.setValue({ code: '12' });

      component.submitTotpAction();

      expect(auth.removeTotp).not.toHaveBeenCalled();
      expect(component.isTotpManageStep).toBeTrue();
    });

    const actions = [
      ['enable', 'enableTotp', 'TOTP MFA has been re-enabled.'],
      ['disable', 'disableTotp', 'TOTP MFA has been disabled.'],
      ['remove', 'removeTotp', 'TOTP MFA has been removed.'],
    ] as const;

    for (const [action, spyName, message] of actions) {
      it(`routes ${action} to ${spyName} and reports the outcome`, () => {
        auth[spyName].and.returnValue(of(makeSettings()));

        component.beginTotpAction(action);
        component.totpManageForm.setValue({ code: '123456' });
        component.submitTotpAction();

        expect(auth[spyName]).toHaveBeenCalledOnceWith('123456');
        expect(component.success).toBe(message);
        expect(component.isTotpManageStep).toBeFalse();
        expect(component.totpMutating).toBeFalse();
      });
    }

    it('keeps the confirmation step open when the action fails', () => {
      auth.disableTotp.and.returnValue(
        throwError(() => new ApiClientClientError('Wrong code.', 400, 'INVALID_CODE')),
      );

      component.beginTotpAction('disable');
      component.totpManageForm.setValue({ code: '000000' });
      component.submitTotpAction();

      expect(component.error).toBe('Wrong code.');
      expect(component.isTotpManageStep).toBeTrue();
      expect(component.totpMutating).toBeFalse();
    });

    it('abandons the action on cancel', () => {
      component.beginTotpAction('remove');
      component.totpManageForm.setValue({ code: '123456' });

      component.cancelTotpAction();

      expect(component.isTotpManageStep).toBeFalse();
      expect(component.totpManageForm.getRawValue().code).toBe('');
    });
  });

  describe('message handling', () => {
    beforeEach(() => verifyGate());

    it('clears a stale success message when the next action starts', () => {
      auth.disableMfa.and.returnValue(of(makeSettings()));
      component.disableSms();
      expect(component.success).toBe('SMS MFA has been disabled.');

      const pending = new Subject<MfaSettingsResponse>();
      auth.removeMfa.and.returnValue(pending.asObservable());
      component.removeSms();

      expect(component.success).toBe('');
      expect(component.smsMutating).toBeTrue();
      pending.complete();
    });

    it('formats the SMS verification timestamp only when one exists', () => {
      expect(component.smsVerifiedAtLabel).toBeNull();

      component.settings = makeSettings({
        sms: { ...makeSettings().sms, phoneVerifiedAtUtc: '2026-01-01T00:00:00Z' },
      });

      expect(component.smsVerifiedAtLabel).toBe(new Date('2026-01-01T00:00:00Z').toLocaleString());
    });

    it('formats the TOTP enrolled and disabled timestamps', () => {
      expect(component.totpEnrolledAtLabel).toBeNull();
      expect(component.totpDisabledAtLabel).toBeNull();

      component.settings = makeSettings({
        totp: {
          ...makeSettings().totp,
          enrolledAtUtc: '2026-01-01T00:00:00Z',
          disabledAtUtc: '2026-02-01T00:00:00Z',
        },
      });

      expect(component.totpEnrolledAtLabel).not.toBeNull();
      expect(component.totpDisabledAtLabel).not.toBeNull();
    });
  });
});
