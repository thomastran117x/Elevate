import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../../core/api/models/api-client-error.model';
import { setUser } from '../../../../../../core/stores/user.actions';
import { User } from '../../../../../../core/stores/user.model';
import { selectUser } from '../../../../../../core/stores/user.selectors';
import { ProfileService } from '../../../../services/profile.service';

@Component({
  selector: 'app-profile-tab',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './profile-tab.component.html',
})
export class ProfileTabComponent implements OnInit {
  private readonly fb = new FormBuilder();
  private readonly destroyRef = inject(DestroyRef);

  readonly profileForm = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.maxLength(100)]),
    username: this.fb.nonNullable.control('', [
      Validators.required,
      Validators.maxLength(50),
    ]),
    avatar: this.fb.nonNullable.control('', [Validators.maxLength(500)]),
  });

  currentUser: User | null = null;
  editing = false;
  saving = false;
  error = '';
  success = '';

  constructor(
    private store: Store,
    private profileService: ProfileService,
  ) {}

  ngOnInit(): void {
    this.store
      .select(selectUser)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((user) => {
        this.currentUser = user;
        if (user && !this.editing) {
          this.profileForm.patchValue({
            name: user.Name ?? '',
            username: user.Username,
            avatar: user.Avatar ?? '',
          });
        }
      });
  }

  get userInitials(): string {
    if (!this.currentUser) return '?';
    const name = this.currentUser.Name || this.currentUser.Username;
    return name.slice(0, 2).toUpperCase();
  }

  get usertypeLabel(): string {
    const type = this.currentUser?.Usertype ?? '';
    return type.charAt(0).toUpperCase() + type.slice(1);
  }

  startEditing(): void {
    this.editing = true;
    this.error = '';
    this.success = '';
  }

  cancelEditing(): void {
    this.editing = false;
    this.error = '';
    if (this.currentUser) {
      this.profileForm.patchValue({
        name: this.currentUser.Name ?? '',
        username: this.currentUser.Username,
        avatar: this.currentUser.Avatar ?? '',
      });
    }
  }

  saveProfile(): void {
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      return;
    }

    const { name, username, avatar } = this.profileForm.getRawValue();
    this.saving = true;
    this.error = '';
    this.success = '';

    this.profileService
      .updateProfile({
        name: name || undefined,
        username: username || undefined,
        avatar: avatar || undefined,
      })
      .pipe(finalize(() => (this.saving = false)))
      .subscribe({
        next: (updated) => {
          this.store.dispatch(
            setUser({
              user: {
                Id: updated.Id,
                Email: updated.Email,
                Username: updated.Username,
                Name: updated.Name ?? null,
                Avatar: updated.Avatar ?? null,
                Usertype: updated.Usertype,
              },
            }),
          );
          this.editing = false;
          this.success = 'Profile updated successfully.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to update profile.');
        },
      });
  }
}
