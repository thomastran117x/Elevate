import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';
import {
  AuthService,
  MfaChallengeResponse,
  MfaStatusResponse,
} from '../../services/auth.service';
import { SecuritySettingsComponent } from './security-settings.component';

describe('SecuritySettingsComponent', () => {
  let fixture: ComponentFixture<SecuritySettingsComponent>;
  let component: SecuritySettingsComponent;
  let auth: jasmine.SpyObj<AuthService>;

  const disabledStatus: MfaStatusResponse = {
    EnrollmentAvailable: true,
    IsSmsMfaEnabled: false,
    MaskedPhoneNumber: null,
    PhoneVerifiedAtUtc: null,
  };

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', [
      'getMfaStatus',
      'startMfaEnrollment',
      'verifyMfaEnrollment',
      'disableMfa',
    ]);
    auth.getMfaStatus.and.returnValue(of(disabledStatus));
    auth.startMfaEnrollment.and.returnValue(
      of({
        Challenge: 'challenge-1',
        ExpiresAtUtc: '2026-06-22T15:30:00Z',
        Channel: 'sms',
        MaskedDestination: '***-***-0123',
      }),
    );
    auth.verifyMfaEnrollment.and.returnValue(
      of({
        EnrollmentAvailable: true,
        IsSmsMfaEnabled: true,
        MaskedPhoneNumber: '***-***-0123',
        PhoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
      }),
    );
    auth.disableMfa.and.returnValue(of(disabledStatus));

    await TestBed.configureTestingModule({
      imports: [SecuritySettingsComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();

    fixture = TestBed.createComponent(SecuritySettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('shows the unenrolled state after loading MFA status', () => {
    const element = fixture.nativeElement as HTMLElement;

    expect(auth.getMfaStatus).toHaveBeenCalled();
    expect(element.textContent).toContain('Not enabled yet');
    expect(element.textContent).toContain('Send verification code');
  });

  it('starts and verifies enrollment, then shows the enabled state', () => {
    component.phoneForm.setValue({ phoneNumber: '+14165550123' });
    component.startEnrollment();
    fixture.detectChanges();

    expect(auth.startMfaEnrollment).toHaveBeenCalledWith('+14165550123');
    expect(component.challenge?.Challenge).toBe('challenge-1');

    component.codeForm.setValue({ code: '654321' });
    component.verifyEnrollment();
    fixture.detectChanges();

    expect(auth.verifyMfaEnrollment).toHaveBeenCalledWith('654321', 'challenge-1');
    expect(component.status?.IsSmsMfaEnabled).toBeTrue();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Protected');
    expect(element.textContent).toContain('***-***-0123');
  });

  it('surfaces invalid verification codes without leaving the verify step', () => {
    const challenge: MfaChallengeResponse = {
      Challenge: 'challenge-1',
      ExpiresAtUtc: '2026-06-22T15:30:00Z',
      Channel: 'sms',
      MaskedDestination: '***-***-0123',
    };
    auth.startMfaEnrollment.and.returnValue(of(challenge));
    auth.verifyMfaEnrollment.and.returnValue(
      throwError(() => new ApiClientClientError('Invalid code.', 401, 'UNAUTHORIZED')),
    );

    component.phoneForm.setValue({ phoneNumber: '+14165550123' });
    component.startEnrollment();
    component.codeForm.setValue({ code: '111111' });
    component.verifyEnrollment();
    fixture.detectChanges();

    expect(component.challenge?.Challenge).toBe('challenge-1');
    expect(component.error).toBe('Invalid code.');
  });

  it('disables MFA and updates the status card', () => {
    component.status = {
      EnrollmentAvailable: true,
      IsSmsMfaEnabled: true,
      MaskedPhoneNumber: '***-***-0123',
      PhoneVerifiedAtUtc: '2026-06-22T15:31:00Z',
    };

    component.disableMfa();
    fixture.detectChanges();

    expect(auth.disableMfa).toHaveBeenCalled();
    expect(component.status?.IsSmsMfaEnabled).toBeFalse();
  });
});
