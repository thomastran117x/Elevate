import { ChangeDetectionStrategy, Component } from '@angular/core';

import { ActiveSessionsCardComponent } from './sessions-card/active-sessions-card.component';
import { TrustedDevicesCardComponent } from './trusted-devices-card/trusted-devices-card.component';

/**
 * Where the account is signed in, and which devices skip sign-in verification. Unlike the
 * security tab this is not behind the step-up gate: anyone signed in can review the lists, and
 * only a change asks for verification.
 */
@Component({
  selector: 'app-devices-tab',
  standalone: true,
  imports: [ActiveSessionsCardComponent, TrustedDevicesCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-6">
      <app-active-sessions-card />
      <app-trusted-devices-card />
    </div>
  `,
})
export class DevicesTabComponent {}
