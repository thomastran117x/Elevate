import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { provideTestStore } from '@testing';

import { ApiClientClientError } from '../../../../../../core/api/models/api-client-error.model';
import { MyProfile, ProfileService } from '../../../../services/profile.service';
import { ProfileTabComponent } from './profile-tab.component';

function makeProfile(overrides: Partial<MyProfile> = {}): MyProfile {
  return {
    Id: 7,
    Email: 'member@example.com',
    Username: 'member',
    CanChangeUsername: true,
    UsernameChangeAvailableAtUtc: null,
    Name: 'Member',
    Avatar: null,
    Usertype: 'Participant',
    Phone: null,
    Address: null,
    GoogleLinked: false,
    MicrosoftLinked: false,
    CreatedAtUtc: '2026-01-01T00:00:00Z',
    UpdatedAtUtc: '2026-01-02T00:00:00Z',
    ...overrides,
  };
}

describe('ProfileTabComponent', () => {
  let fixture: ComponentFixture<ProfileTabComponent>;
  let component: ProfileTabComponent;
  let profileService: jasmine.SpyObj<ProfileService>;

  beforeEach(async () => {
    profileService = jasmine.createSpyObj<ProfileService>('ProfileService', [
      'getMyProfile',
      'updateProfile',
      'changeUsername',
      'uploadAvatar',
    ]);
    profileService.getMyProfile.and.returnValue(of(makeProfile()));

    await TestBed.configureTestingModule({
      imports: [ProfileTabComponent],
      providers: [
        { provide: ProfileService, useValue: profileService },
        provideRouter([]),
        ...provideTestStore(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProfileTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('keeps ordinary profile updates separate from username changes', () => {
    profileService.updateProfile.and.returnValue(of(makeProfile({ Name: 'New Name' })));
    component.startEditing();
    component.profileForm.patchValue({ name: 'New Name', phone: '555-1111' });

    component.saveProfile();

    expect(profileService.updateProfile).toHaveBeenCalledWith({
      name: 'New Name',
      phone: '555-1111',
      address: undefined,
    });
    expect(profileService.changeUsername).not.toHaveBeenCalled();
  });

  it('normalizes a verified username change and applies the cooldown response', () => {
    const changed = makeProfile({
      Username: 'new-name',
      CanChangeUsername: false,
      UsernameChangeAvailableAtUtc: '2026-09-14T12:00:00Z',
    });
    profileService.changeUsername.and.returnValue(of(changed));
    component.startUsernameChange();
    component.usernameMfaVerified = true;
    component.usernameForm.setValue({ username: '  NEW-NAME  ' });

    component.changeUsername();

    expect(profileService.changeUsername).toHaveBeenCalledOnceWith('new-name');
    expect(component.profile).toEqual(changed);
    expect(component.usernameChangeRequested).toBeFalse();
    expect(component.success).toContain('@new-name');
  });

  it('does not open the rename flow while cooldown is active', () => {
    component.profile = makeProfile({
      CanChangeUsername: false,
      UsernameChangeAvailableAtUtc: '2026-09-14T12:00:00Z',
    });

    component.startUsernameChange();

    expect(component.usernameChangeRequested).toBeFalse();
  });

  it('returns to the MFA gate when the server says step-up verification expired', () => {
    profileService.changeUsername.and.returnValue(
      throwError(() => new ApiClientClientError('Verify first', 403, 'MFA_REQUIRED')),
    );
    component.startUsernameChange();
    component.usernameMfaVerified = true;
    component.usernameForm.setValue({ username: 'next-name' });

    component.changeUsername();

    expect(component.usernameMfaVerified).toBeFalse();
    expect(component.error).toBe('Verify first');
  });

  it('labels the cooldown availability date as UTC', () => {
    component.profile = makeProfile({
      CanChangeUsername: false,
      UsernameChangeAvailableAtUtc: '2026-09-14T00:00:00Z',
    });

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('September 14, 2026 UTC');
  });
});
