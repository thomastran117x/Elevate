import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../../../core/api/models/api-client-error.model';
import { setUser } from '../../../../../../core/stores/user.actions';
import { User } from '../../../../../../core/stores/user.model';
import { MyProfile, ProfileService } from '../../../../services/profile.service';
import { MfaGateComponent } from '../../mfa-gate/mfa-gate.component';

const MAX_AVATAR_BYTES = 5 * 1024 * 1024;
const MFA_REQUIRED_ERROR_CODE = 'MFA_REQUIRED';

@Component({
  selector: 'app-profile-tab',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, MfaGateComponent],
  templateUrl: './profile-tab.component.html',
})
export class ProfileTabComponent implements OnInit {
  private readonly fb = new FormBuilder();
  private readonly destroyRef = inject(DestroyRef);

  readonly profileForm = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.maxLength(100)]),
    phone: this.fb.nonNullable.control('', [Validators.maxLength(30)]),
    address: this.fb.nonNullable.control('', [Validators.maxLength(200)]),
  });

  readonly usernameForm = this.fb.nonNullable.group({
    username: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(50)]),
  });

  profile: MyProfile | null = null;
  loading = true;
  editing = false;
  saving = false;
  avatarUploading = false;
  usernameChangeRequested = false;
  usernameMfaVerified = false;
  usernameSaving = false;
  error = '';
  success = '';

  constructor(
    private store: Store,
    private profileService: ProfileService,
  ) {}

  ngOnInit(): void {
    this.loadProfile();
  }

  get userInitials(): string {
    const name = this.profile?.Name || this.profile?.Username || '';
    return name ? name.slice(0, 2).toUpperCase() : '?';
  }

  get usertypeLabel(): string {
    const type = this.profile?.Usertype ?? '';
    return type.charAt(0).toUpperCase() + type.slice(1);
  }

  private loadProfile(): void {
    this.loading = true;
    this.profileService
      .getMyProfile()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (profile) => {
          this.profile = profile;
          this.resetForm();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load your profile.');
        },
      });
  }

  private resetForm(): void {
    if (!this.profile) return;
    this.profileForm.patchValue({
      name: this.profile.Name ?? '',
      phone: this.profile.Phone ?? '',
      address: this.profile.Address ?? '',
    });
    this.usernameForm.setValue({ username: this.profile.Username });
  }

  startEditing(): void {
    this.editing = true;
    this.error = '';
    this.success = '';
  }

  cancelEditing(): void {
    this.editing = false;
    this.error = '';
    this.resetForm();
  }

  saveProfile(): void {
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      return;
    }

    const { name, phone, address } = this.profileForm.getRawValue();
    this.saving = true;
    this.error = '';
    this.success = '';

    this.profileService
      .updateProfile({
        name: name || undefined,
        phone: phone || undefined,
        address: address || undefined,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.saving = false)),
      )
      .subscribe({
        next: (updated) => {
          this.profile = updated;
          this.syncStore(updated);
          this.editing = false;
          this.success = 'Profile updated successfully.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to update profile.');
        },
      });
  }

  startUsernameChange(): void {
    if (!this.profile?.CanChangeUsername) return;
    this.usernameChangeRequested = true;
    this.usernameMfaVerified = false;
    this.usernameForm.setValue({ username: this.profile.Username });
    this.error = '';
    this.success = '';
  }

  cancelUsernameChange(): void {
    this.usernameChangeRequested = false;
    this.usernameMfaVerified = false;
    this.error = '';
    if (this.profile) {
      this.usernameForm.setValue({ username: this.profile.Username });
    }
  }

  changeUsername(): void {
    if (!this.usernameMfaVerified) {
      return;
    }

    const username = this.usernameForm.getRawValue().username.trim().toLowerCase();
    this.usernameForm.controls.username.setValue(username);
    if (this.usernameForm.invalid) {
      this.usernameForm.markAllAsTouched();
      return;
    }

    this.usernameSaving = true;
    this.error = '';
    this.success = '';

    this.profileService
      .changeUsername(username)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.usernameSaving = false)),
      )
      .subscribe({
        next: (updated) => {
          this.profile = updated;
          this.syncStore(updated);
          this.usernameChangeRequested = false;
          this.usernameMfaVerified = false;
          this.usernameForm.setValue({ username: updated.Username });
          this.success = `Username changed to @${updated.Username}.`;
        },
        error: (err) => {
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.usernameMfaVerified = false;
          }
          this.error = getApiClientMessage(err, 'Unable to change username.');
        },
      });
  }

  onAvatarSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.error = '';
    this.success = '';

    if (!file.type.startsWith('image/')) {
      this.error = 'Please choose an image file.';
      return;
    }
    if (file.size > MAX_AVATAR_BYTES) {
      this.error = 'Image must be smaller than 5MB.';
      return;
    }

    this.avatarUploading = true;
    this.profileService
      .uploadAvatar(file)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.avatarUploading = false)),
      )
      .subscribe({
        next: (updated) => {
          this.profile = updated;
          this.syncStore(updated);
          this.success = 'Profile photo updated.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to upload profile photo.');
        },
      });
  }

  private syncStore(profile: MyProfile): void {
    const user: User = {
      Id: profile.Id,
      Email: profile.Email,
      Username: profile.Username,
      Name: profile.Name ?? null,
      Avatar: profile.Avatar ?? null,
      Usertype: profile.Usertype,
    };
    this.store.dispatch(setUser({ user }));
  }
}
