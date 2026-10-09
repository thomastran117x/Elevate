import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { makeCurrentUser, provideTestStore } from '@testing';

import { DangerZoneTabComponent } from './danger-zone-tab.component';
import { ApiClientClientError } from '../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../core/api/services/auth-token.service';
import { ProfileService } from '../../../../services/profile.service';
import { MfaGateComponent } from '../../mfa-gate/mfa-gate.component';

@Component({ selector: 'app-mfa-gate', standalone: true, template: '' })
class StubMfaGateComponent {
  @Input() autoOpen = false;
  @Output() verified = new EventEmitter<void>();
}

describe('DangerZoneTabComponent', () => {
  let fixture: ComponentFixture<DangerZoneTabComponent>;
  let component: DangerZoneTabComponent;
  let profileService: jasmine.SpyObj<Pick<ProfileService, 'deleteAccount'>>;
  let logoutLocal: jasmine.Spy;
  let router: Router;

  beforeEach(async () => {
    profileService = jasmine.createSpyObj('ProfileService', ['deleteAccount']);
    logoutLocal = jasmine.createSpy('logoutLocal');

    await TestBed.configureTestingModule({
      imports: [DangerZoneTabComponent],
      providers: [
        provideRouter([]),
        ...provideTestStore({ user: makeCurrentUser({ Username: 'member' }) }),
        { provide: ProfileService, useValue: profileService },
        { provide: AuthTokenService, useValue: { logoutLocal } },
      ],
    })
      .overrideComponent(DangerZoneTabComponent, {
        remove: { imports: [MfaGateComponent] },
        add: { imports: [StubMfaGateComponent] },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(DangerZoneTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function gate(): StubMfaGateComponent | null {
    return (
      fixture.debugElement.query(By.directive(StubMfaGateComponent))?.componentInstance ?? null
    );
  }

  function confirmDeletion(): void {
    component.onMfaVerified();
    component.showDeleteConfirm();
    component.confirmationInput = 'member';
  }

  it('shows only the gate until the session is verified', () => {
    expect(gate()).not.toBeNull();
    expect(gate()!.autoOpen).toBeFalse();

    gate()!.verified.emit();
    fixture.detectChanges();

    expect(component.mfaVerified).toBeTrue();
    expect(gate()).toBeNull();
  });

  it('requires the exact username before deleting', () => {
    component.onMfaVerified();
    component.showDeleteConfirm();
    component.confirmationInput = 'someone-else';

    component.deleteAccount();

    expect(component.confirmationMatches).toBeFalse();
    expect(profileService.deleteAccount).not.toHaveBeenCalled();
  });

  it('resets the confirmation on cancel', () => {
    confirmDeletion();

    component.cancelDelete();

    expect(component.showConfirm).toBeFalse();
    expect(component.confirmationInput).toBe('');
  });

  it('signs out and leaves after deleting', () => {
    profileService.deleteAccount.and.returnValue(of(undefined as void));
    confirmDeletion();

    component.deleteAccount();

    expect(logoutLocal).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/']);
    expect(component.deleting).toBeFalse();
  });

  it('re-locks with an immediate prompt and keeps the confirmation when the proof expired', () => {
    profileService.deleteAccount.and.returnValue(
      throwError(() => new ApiClientClientError('Verify again', 403, 'MFA_REQUIRED')),
    );
    confirmDeletion();

    component.deleteAccount();
    fixture.detectChanges();

    expect(component.mfaVerified).toBeFalse();
    expect(component.stepUpLapsed).toBeTrue();
    expect(component.error).toBe('');
    expect(gate()!.autoOpen).toBeTrue();
    expect(component.showConfirm).toBeTrue();
    expect(component.confirmationInput).toBe('member');
    expect(logoutLocal).not.toHaveBeenCalled();

    gate()!.verified.emit();
    fixture.detectChanges();

    expect(component.resumeNotice).toBe('Identity verified. Submit again to finish.');
    expect(fixture.nativeElement.textContent).toContain('Submit again to finish.');
  });

  it('reports any other failure without re-locking', () => {
    profileService.deleteAccount.and.returnValue(
      throwError(() => new ApiClientClientError('Try later.', 503, 'UNAVAILABLE')),
    );
    confirmDeletion();

    component.deleteAccount();

    expect(component.error).toBe('Try later.');
    expect(component.mfaVerified).toBeTrue();
    expect(component.deleting).toBeFalse();
  });
});
