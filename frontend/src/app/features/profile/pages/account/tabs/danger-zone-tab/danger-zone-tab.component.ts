import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
} from '../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../core/api/services/auth-token.service';
import { User } from '../../../../../../core/stores/user.model';
import { selectUser } from '../../../../../../core/stores/user.selectors';
import { ProfileService } from '../../../../services/profile.service';
import { MfaGateComponent } from '../../mfa-gate/mfa-gate.component';

const MFA_REQUIRED_ERROR_CODE = 'MFA_REQUIRED';

@Component({
  selector: 'app-danger-zone-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MfaGateComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './danger-zone-tab.component.html',
})
export class DangerZoneTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  // The delete flow is revealed only after the reusable gate confirms a fresh
  // MFA verification; the delete endpoint is also [RequireMfa]-gated.
  mfaVerified = false;
  // Set when the server refused deletion because the step-up proof had expired; the
  // gate then prompts at once and the typed confirmation is kept.
  stepUpLapsed = false;
  resumeNotice = '';
  currentUser: User | null = null;
  showConfirm = false;
  confirmationInput = '';
  deleting = false;
  error = '';

  constructor(
    private store: Store,
    private profileService: ProfileService,
    private authToken: AuthTokenService,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.store
      .select(selectUser)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((user) => {
        this.currentUser = user;
      });
  }

  onMfaVerified(): void {
    this.mfaVerified = true;
    this.resumeNotice = this.stepUpLapsed ? 'Identity verified. Submit again to finish.' : '';
  }

  get confirmationMatches(): boolean {
    const username = this.currentUser?.Username;
    return !!username && this.confirmationInput === username;
  }

  showDeleteConfirm(): void {
    this.showConfirm = true;
    this.confirmationInput = '';
    this.error = '';
  }

  cancelDelete(): void {
    this.showConfirm = false;
    this.confirmationInput = '';
    this.error = '';
  }

  deleteAccount(): void {
    if (!this.confirmationMatches) return;

    this.deleting = true;
    this.error = '';
    this.resumeNotice = '';

    this.profileService
      .deleteAccount()
      .pipe(finalize(() => (this.deleting = false)))
      .subscribe({
        next: () => {
          this.authToken.logoutLocal();
          this.router.navigate(['/']);
        },
        error: (err) => {
          if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.stepUpLapsed = true;
            this.mfaVerified = false;
            return;
          }
          this.error = getApiClientMessage(err, 'Unable to delete account.');
        },
      });
  }
}
