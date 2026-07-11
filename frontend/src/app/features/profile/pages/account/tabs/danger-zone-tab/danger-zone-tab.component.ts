import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../core/api/services/auth-token.service';
import { User } from '../../../../../../core/stores/user.model';
import { selectUser } from '../../../../../../core/stores/user.selectors';
import { ProfileService } from '../../../../services/profile.service';
import { MfaGateComponent } from '../../mfa-gate/mfa-gate.component';

@Component({
  selector: 'app-danger-zone-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MfaGateComponent],
  templateUrl: './danger-zone-tab.component.html',
})
export class DangerZoneTabComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  // The delete flow is revealed only after the reusable gate confirms a fresh
  // MFA verification; the delete endpoint is also [RequireMfa]-gated.
  mfaVerified = false;
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

    this.profileService
      .deleteAccount()
      .pipe(finalize(() => (this.deleting = false)))
      .subscribe({
        next: () => {
          this.authToken.logoutLocal();
          this.router.navigate(['/']);
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to delete account.');
        },
      });
  }
}
