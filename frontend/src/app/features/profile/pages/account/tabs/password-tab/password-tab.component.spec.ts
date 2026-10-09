import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { PasswordTabComponent } from './password-tab.component';
import { ApiClientClientError } from '../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../core/api/services/auth-token.service';
import { ProfileService } from '../../../../services/profile.service';
import { MfaGateComponent } from '../../mfa-gate/mfa-gate.component';

@Component({ selector: 'app-mfa-gate', standalone: true, template: '' })
class StubMfaGateComponent {
  @Input() autoOpen = false;
  @Output() verified = new EventEmitter<void>();
}

const mfaRequired = () => new ApiClientClientError('Verify again', 403, 'MFA_REQUIRED');

describe('PasswordTabComponent', () => {
  let fixture: ComponentFixture<PasswordTabComponent>;
  let component: PasswordTabComponent;
  let profileService: jasmine.SpyObj<Pick<ProfileService, 'changePassword'>>;
  let logoutLocal: jasmine.Spy;
  let router: Router;

  beforeEach(async () => {
    profileService = jasmine.createSpyObj('ProfileService', ['changePassword']);
    logoutLocal = jasmine.createSpy('logoutLocal');

    await TestBed.configureTestingModule({
      imports: [PasswordTabComponent],
      providers: [
        provideRouter([]),
        { provide: ProfileService, useValue: profileService },
        { provide: AuthTokenService, useValue: { logoutLocal } },
      ],
    })
      .overrideComponent(PasswordTabComponent, {
        remove: { imports: [MfaGateComponent] },
        add: { imports: [StubMfaGateComponent] },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(PasswordTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function gate(): StubMfaGateComponent | null {
    return (
      fixture.debugElement.query(By.directive(StubMfaGateComponent))?.componentInstance ?? null
    );
  }

  function fillForm(): void {
    component.passwordForm.setValue({
      currentPassword: 'Password123!',
      newPassword: 'NewPassword456!',
      confirmPassword: 'NewPassword456!',
    });
  }

  it('shows only the gate until the session is verified', () => {
    expect(gate()).not.toBeNull();
    expect(gate()!.autoOpen).toBeFalse();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();

    gate()!.verified.emit();
    fixture.detectChanges();

    expect(component.mfaVerified).toBeTrue();
    expect(component.resumeNotice).toBe('');
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
  });

  it('flags mismatched passwords once a field is touched', () => {
    component.passwordForm.setValue({
      currentPassword: 'Password123!',
      newPassword: 'NewPassword456!',
      confirmPassword: 'Different789!',
    });
    expect(component.passwordsMismatch).toBeFalse();

    component.passwordForm.controls.confirmPassword.markAsTouched();

    expect(component.passwordsMismatch).toBeTrue();
  });

  it('does not submit an invalid form', () => {
    component.changePassword();

    expect(profileService.changePassword).not.toHaveBeenCalled();
    expect(component.passwordForm.touched).toBeTrue();
  });

  it('signs out locally and goes to sign in after a successful change', () => {
    profileService.changePassword.and.returnValue(of(undefined as void));
    component.onMfaVerified();
    fillForm();

    component.changePassword();

    expect(profileService.changePassword).toHaveBeenCalledOnceWith(
      'Password123!',
      'NewPassword456!',
    );
    expect(logoutLocal).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login']);
    expect(component.saving).toBeFalse();
  });

  it('re-locks with an immediate prompt and keeps the typed values when the proof expired', () => {
    profileService.changePassword.and.returnValue(throwError(mfaRequired));
    component.onMfaVerified();
    fixture.detectChanges();
    fillForm();

    component.changePassword();
    fixture.detectChanges();

    expect(component.mfaVerified).toBeFalse();
    expect(component.stepUpLapsed).toBeTrue();
    expect(component.error).toBe('');
    expect(gate()!.autoOpen).toBeTrue();
    expect(component.passwordForm.getRawValue().newPassword).toBe('NewPassword456!');
    expect(logoutLocal).not.toHaveBeenCalled();

    gate()!.verified.emit();
    fixture.detectChanges();

    expect(component.mfaVerified).toBeTrue();
    expect(component.resumeNotice).toBe('Identity verified. Submit again to finish.');
    expect(fixture.nativeElement.textContent).toContain('Submit again to finish.');
  });

  it('clears the resume notice on the next attempt', () => {
    profileService.changePassword.and.returnValue(
      throwError(
        () => new ApiClientClientError('Current password is incorrect.', 401, 'UNAUTHORIZED'),
      ),
    );
    component.stepUpLapsed = true;
    component.onMfaVerified();
    fillForm();

    component.changePassword();

    expect(component.resumeNotice).toBe('');
    expect(component.error).toBe('Current password is incorrect.');
    expect(component.mfaVerified).toBeTrue();
  });
});
