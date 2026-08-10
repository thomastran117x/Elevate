import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { FeatureFlagsService } from '../../../../core/features/feature-flags.service';
import { FEATURE_KEYS } from '../../../../core/features/feature-flags.types';
import { EventFavouriteToggleComponent } from '../../components/event-favourite-toggle/event-favourite-toggle.component';
import { EventInvitation } from '../../models/event-invitation.types';
import { EventInvitationsService } from '../../services/event-invitations.service';
import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';

@Component({
  selector: 'app-my-invites',
  standalone: true,
  imports: [CommonModule, RouterLink, EventFavouriteToggleComponent],
  templateUrl: './my-invites.component.html',
  styleUrls: ['./my-invites.component.css'],
})
export class MyInvitesComponent {
  invitations: EventInvitation[] = [];
  loading = true;
  actionLoadingId: number | null = null;
  error = '';
  readonly favouritesFeatureEnabled: boolean;

  constructor(
    private invitationsService: EventInvitationsService,
    private router: Router,
    private authReturnUrl: AuthReturnUrlService,
    private featureFlags: FeatureFlagsService,
  ) {
    this.favouritesFeatureEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.eventsFavourites);
  }

  /**
   * A pending invite has not been accepted yet, so the backend's visibility gate would reject
   * a star on it. Only offer the control once the event is actually open to them.
   */
  canFavourite(invitation: EventInvitation): boolean {
    return (
      this.favouritesFeatureEnabled &&
      !!invitation.event &&
      invitation.effectiveStatus !== 'Pending'
    );
  }

  onFavouriteFailed(response: unknown): void {
    this.error = getApiClientMessage(response, 'We could not update your saved events.');
  }

  ngOnInit(): void {
    this.load();
  }

  goToLogin(): void {
    this.authReturnUrl.set(this.router.url);
    void this.router.navigate(['/auth/login'], {
      queryParams: {
        returnUrl: this.router.url,
      },
    });
  }

  accept(invitation: EventInvitation): void {
    this.actionLoadingId = invitation.id;
    this.error = '';
    this.invitationsService.acceptById(invitation.id).subscribe({
      next: () => {
        this.actionLoadingId = null;
        this.load();
      },
      error: (err) => {
        this.actionLoadingId = null;
        this.error = err?.error?.message || 'We could not accept this invitation.';
      },
    });
  }

  decline(invitation: EventInvitation): void {
    this.actionLoadingId = invitation.id;
    this.error = '';
    this.invitationsService.declineById(invitation.id).subscribe({
      next: () => {
        this.actionLoadingId = null;
        this.load();
      },
      error: (err) => {
        this.actionLoadingId = null;
        this.error = err?.error?.message || 'We could not decline this invitation.';
      },
    });
  }

  private load(): void {
    this.loading = true;
    this.error = '';

    this.invitationsService.getMine().subscribe({
      next: (invitations) => {
        this.invitations = invitations;
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'We could not load your invites.';
      },
    });
  }
}
