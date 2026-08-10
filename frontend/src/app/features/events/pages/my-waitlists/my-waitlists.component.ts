import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { FeatureFlagsService } from '../../../../core/features/feature-flags.service';
import { FEATURE_KEYS } from '../../../../core/features/feature-flags.types';
import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { EventFavouriteToggleComponent } from '../../components/event-favourite-toggle/event-favourite-toggle.component';
import { WaitlistedEvent } from '../../models/event-waitlist.types';
import { EventWaitlistService } from '../../services/event-waitlist.service';

@Component({
  selector: 'app-my-waitlists',
  standalone: true,
  imports: [CommonModule, RouterLink, EventFavouriteToggleComponent],
  templateUrl: './my-waitlists.component.html',
})
export class MyWaitlistsComponent implements OnInit {
  items: WaitlistedEvent[] = [];
  loading = true;
  actionLoadingId: number | null = null;
  error = '';
  requiresLogin = false;
  readonly favouritesFeatureEnabled: boolean;

  constructor(
    private waitlistService: EventWaitlistService,
    private router: Router,
    private authReturnUrl: AuthReturnUrlService,
    private featureFlags: FeatureFlagsService,
  ) {
    this.favouritesFeatureEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.eventsFavourites);
  }

  ngOnInit(): void {
    this.load();
  }

  goToLogin(): void {
    this.authReturnUrl.set(this.router.url);
    void this.router.navigate(['/auth/login'], {
      queryParams: { returnUrl: this.router.url },
    });
  }

  isCancelled(item: WaitlistedEvent): boolean {
    return item.event?.lifecycleState === 'Cancelled';
  }

  onFavouriteFailed(response: unknown): void {
    this.error = getApiClientMessage(response, 'We could not update your saved events.');
  }

  leave(item: WaitlistedEvent): void {
    if (this.actionLoadingId !== null) return;
    this.actionLoadingId = item.entryId;
    this.error = '';

    this.waitlistService.leave(item.event.id).subscribe({
      next: () => {
        this.actionLoadingId = null;
        this.load();
      },
      error: (response) => {
        this.actionLoadingId = null;
        this.error = getApiClientMessage(response, 'We could not leave this waitlist.');
      },
    });
  }

  private load(): void {
    this.loading = true;
    this.error = '';

    this.waitlistService.getMine().subscribe({
      next: (items) => {
        this.items = items;
        this.loading = false;
      },
      error: (response) => {
        this.loading = false;
        this.requiresLogin = response?.status === 401;
        if (!this.requiresLogin) {
          this.error = getApiClientMessage(response, 'We could not load your waitlists.');
        }
      },
    });
  }
}
