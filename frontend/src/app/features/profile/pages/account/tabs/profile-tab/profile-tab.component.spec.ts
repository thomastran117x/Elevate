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

  it('does not submit an invalid ordinary profile form', () => {
    component.profileForm.controls.name.setValue('x'.repeat(101));

    component.saveProfile();

    expect(component.profileForm.controls.name.touched).toBeTrue();
    expect(profileService.updateProfile).not.toHaveBeenCalled();
  });

  it('surfaces ordinary profile update failures', () => {
    profileService.updateProfile.and.returnValue(
      throwError(() => new ApiClientClientError('Update failed', 409, 'PROFILE_CONFLICT')),
    );

    component.saveProfile();

    expect(component.error).toBe('Update failed');
    expect(component.saving).toBeFalse();
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

  it('does not submit a username until MFA verification completes', () => {
    component.startUsernameChange();
    component.usernameForm.setValue({ username: 'next-name' });

    component.changeUsername();

    expect(profileService.changeUsername).not.toHaveBeenCalled();
  });

  it('normalizes before rejecting an empty username', () => {
    component.startUsernameChange();
    component.usernameMfaVerified = true;
    component.usernameForm.setValue({ username: '   ' });

    component.changeUsername();

    expect(component.usernameForm.controls.username.value).toBe('');
    expect(component.usernameForm.controls.username.hasError('required')).toBeTrue();
    expect(profileService.changeUsername).not.toHaveBeenCalled();
  });

  it('restores the current username when the rename flow is cancelled', () => {
    component.startUsernameChange();
    component.usernameMfaVerified = true;
    component.usernameForm.setValue({ username: 'abandoned-name' });

    component.cancelUsernameChange();

    expect(component.usernameChangeRequested).toBeFalse();
    expect(component.usernameMfaVerified).toBeFalse();
    expect(component.usernameForm.controls.username.value).toBe('member');
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

  it('keeps MFA verification for a non-MFA username API failure', () => {
    profileService.changeUsername.and.returnValue(
      throwError(() => new ApiClientClientError('Already taken', 409, 'USERNAME_TAKEN')),
    );
    component.startUsernameChange();
    component.usernameMfaVerified = true;
    component.usernameForm.setValue({ username: 'claimed-name' });

    component.changeUsername();

    expect(component.usernameMfaVerified).toBeTrue();
    expect(component.error).toBe('Already taken');
    expect(component.usernameSaving).toBeFalse();
  });

  it('labels the cooldown availability date as UTC', () => {
    component.profile = makeProfile({
      CanChangeUsername: false,
      UsernameChangeAvailableAtUtc: '2026-09-14T00:00:00Z',
    });

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('September 14, 2026 UTC');
  });

  it('derives initials from the username and handles an unloaded profile', () => {
    component.profile = makeProfile({ Name: null, Username: 'username' });
    expect(component.userInitials).toBe('US');

    component.profile = null;
    expect(component.userInitials).toBe('?');
    expect(component.usertypeLabel).toBe('');
  });

  it('ignores an avatar event without a file', () => {
    const input = { files: [], value: 'selected' };

    component.onAvatarSelected({ target: input } as unknown as Event);

    expect(input.value).toBe('');
    expect(profileService.uploadAvatar).not.toHaveBeenCalled();
  });

  it('rejects non-image and oversized avatar files locally', () => {
    const nonImage = new File(['not-an-image'], 'avatar.txt', { type: 'text/plain' });
    component.onAvatarSelected({
      target: { files: [nonImage], value: 'selected' },
    } as unknown as Event);
    expect(component.error).toBe('Please choose an image file.');

    const oversized = new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'avatar.png', {
      type: 'image/png',
    });
    component.onAvatarSelected({
      target: { files: [oversized], value: 'selected' },
    } as unknown as Event);

    expect(component.error).toBe('Image must be smaller than 5MB.');
    expect(profileService.uploadAvatar).not.toHaveBeenCalled();
  });

  it('updates the profile after a valid avatar upload', () => {
    const updated = makeProfile({ Avatar: '/avatars/new.png' });
    profileService.uploadAvatar.and.returnValue(of(updated));
    const image = new File(['image'], 'avatar.png', { type: 'image/png' });

    component.onAvatarSelected({
      target: { files: [image], value: 'selected' },
    } as unknown as Event);

    expect(profileService.uploadAvatar).toHaveBeenCalledOnceWith(image);
    expect(component.profile).toEqual(updated);
    expect(component.success).toBe('Profile photo updated.');
    expect(component.avatarUploading).toBeFalse();
  });
});
