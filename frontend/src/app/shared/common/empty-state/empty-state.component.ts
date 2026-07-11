import { Component, Input } from '@angular/core';
import { RouterModule } from '@angular/router';
import { AppButtonComponent } from '@common/button/button.component';
import { IconShellComponent } from '@common/icon-shell/icon-shell.component';

@Component({
  selector: 'empty-state',
  standalone: true,
  imports: [RouterModule, AppButtonComponent, IconShellComponent],
  templateUrl: './empty-state.component.html',
  styleUrl: './empty-state.component.css',
})
export class EmptyStateComponent {
  @Input() icon = '*';
  @Input() title = 'Nothing here yet';
  @Input() subtitle = 'Try adjusting your filters or searching for a different event.';

  @Input() primaryLabel?: string;
  @Input() primaryHref?: any[];

  @Input() secondaryLabel?: string;
  @Input() secondaryHref?: any[];

  @Input() className = '';
}
