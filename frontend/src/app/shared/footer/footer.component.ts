import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FeatureFlagsService } from '../../core/features/feature-flags.service';
import { FEATURE_KEYS } from '../../core/features/feature-flags.types';

@Component({
  selector: 'app-footer',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './footer.component.html',
})
export class FooterComponent {
  currentYear = new Date().getFullYear();
  readonly authEnabled: boolean;
  readonly eventsEnabled: boolean;
  readonly clubsEnabled: boolean;

  constructor(private featureFlags: FeatureFlagsService) {
    this.authEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.auth);
    this.eventsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.events);
    this.clubsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.clubs);
  }
}
