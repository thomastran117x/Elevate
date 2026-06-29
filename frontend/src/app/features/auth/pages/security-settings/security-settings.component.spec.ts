import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';
import { AuthService, MfaSettingsResponse } from '../../services/auth.service';
import { SecuritySettingsComponent } from './security-settings.component';

describe('SecuritySettingsComponent', () => {
  let fixture: ComponentFixture<SecuritySettingsComponent>;
  let component: SecuritySettingsComponent;
  let auth: jasmine.SpyObj<AuthService>;

  const baseSettings: MfaSettingsResponse = {
    email: { maskedEmail: 'u***@example.com', isEnabled: true },
    sms: {
      enrollmentAvailable: true,
      isConfigured: false,
      isEnabled: false,
      canEnroll: true,
      canEnable: false,
      canDisable: false,
      canRemove: false,
    },
    totp: {
      enrollmentAvailable: true,
      isConfigured: false,
      isEnabled: false,
      canEnroll: true,
      canEnable: false,
      canDisable: false,
      canRemove: false,
    },
  };

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', [
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
    ]);
    auth.getMfaStatus.and.returnValue(of(baseSettings));
    auth.startMfaEnrollment.and.returnValue(
      of({
        Challenge: 'challenge-1',
        ExpiresAtUtc: '2026-06-22T15:30:00Z',
        Channel: 'sms',
        MaskedDestination: '***-***-0123',
      }),
    );
    auth.startMfaEnable.and.returnValue(
      of({
        Challenge: 'challenge-2',
        ExpiresAtUtc: '2026-06-22T15:40:00Z',
        Channel: 'sms',
        MaskedDestination: '***-***-0123',
      }),
    );
    auth.verifyMfaEnrollment.and.returnValue(
      of({
        ...baseSettings,
        sms: {
          enrollmentAvailable: true,
          isConfigured: true,
          isEnabled: true,
          maskedPhoneNumber: '***-***-0123',
          phoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
          canEnroll: true,
          canEnable: false,
          canDisable: true,
          canRemove: true,
        },
      }),
    );
    auth.disableMfa.and.returnValue(of(baseSettings));
    auth.removeMfa.and.returnValue(of(baseSettings));
    auth.startTotpEnrollment.and.returnValue(
      of({
        SecretKey: 'BASE32SECRET',
        QrCodeUri: 'otpauth://totp/test',
        ExpiresAtUtc: '2026-06-22T15:31:00Z',
      }),
    );
    auth.verifyTotpEnrollment.and.returnValue(
      of({
        ...baseSettings,
        totp: {
          enrollmentAvailable: true,
          isConfigured: true,
          isEnabled: true,
          enrolledAtUtc: '2026-06-22T15:35:00Z',
          canEnroll: false,
          canEnable: false,
          canDisable: true,
          canRemove: true,
        },
      }),
    );
    auth.enableTotp.and.returnValue(of(baseSettings));
    auth.disableTotp.and.returnValue(of(baseSettings));
    auth.removeTotp.and.returnValue(of(baseSettings));

    await TestBed.configureTestingModule({
      imports: [SecuritySettingsComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();

    fixture = TestBed.createComponent(SecuritySettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('shows the unified mfa page after loading settings', () => {
    const element = fixture.nativeElement as HTMLElement;

    expect(auth.getMfaStatus).toHaveBeenCalled();
    expect(element.textContent).toContain('Multi-factor authentication');
    expect(element.textContent).toContain('Email');
    expect(element.textContent).toContain('SMS');
    expect(element.textContent).toContain('Authenticator app');
    expect(element.textContent).toContain('u***@example.com');
  });

  it('starts and verifies sms enrollment', () => {
    component.openSmsEditor();
    component.phoneForm.setValue({ phoneNumber: '+14165550123' });
    component.startSmsEnrollment();
    fixture.detectChanges();

    expect(auth.startMfaEnrollment).toHaveBeenCalledWith('+14165550123');
    expect(component.smsChallenge?.Challenge).toBe('challenge-1');

    component.smsCodeForm.setValue({ code: '654321' });
    component.verifySmsChallenge();
    fixture.detectChanges();

    expect(auth.verifyMfaEnrollment).toHaveBeenCalledWith('654321', 'challenge-1');
    expect(component.settings?.sms.isEnabled).toBeTrue();
  });

  it('starts and verifies totp enrollment', () => {
    component.startTotpEnrollment();
    fixture.detectChanges();

    expect(auth.startTotpEnrollment).toHaveBeenCalled();
    expect(component.totpEnrollment?.SecretKey).toBe('BASE32SECRET');

    component.totpSetupForm.setValue({ code: '123456' });
    component.verifyTotpEnrollment();
    fixture.detectChanges();

    expect(auth.verifyTotpEnrollment).toHaveBeenCalledWith('123456');
    expect(component.settings?.totp.isEnabled).toBeTrue();
  });

  it('keeps the original totp action message when the dialog is cancelled mid-flight', () => {
    const response$ = new Subject<MfaSettingsResponse>();
    auth.disableTotp.and.returnValue(response$);

    component.beginTotpAction('disable');
    component.totpManageForm.setValue({ code: '111111' });
    component.submitTotpAction();
    component.cancelTotpAction();

    response$.next(baseSettings);
    response$.complete();
    fixture.detectChanges();

    expect(component.success).toBe('TOTP MFA has been disabled.');
  });
  it('keeps the current state visible when a totp action fails', () => {
    auth.disableTotp.and.returnValue(
      throwError(() => new ApiClientClientError('Invalid TOTP code.', 400, 'BAD_REQUEST')),
    );

    component.beginTotpAction('disable');
    component.totpManageForm.setValue({ code: '111111' });
    component.submitTotpAction();
    fixture.detectChanges();

    expect(component.error).toBe('Invalid TOTP code.');
    expect(auth.getMfaStatus).toHaveBeenCalledTimes(2);
  });
});
