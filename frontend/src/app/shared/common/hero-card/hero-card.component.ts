import { Component, Input } from '@angular/core';
import { RouterModule } from '@angular/router';
import { AppButtonComponent } from '@common/button/button.component';
import { PillComponent } from '@common/pill/pill.component';

@Component({
  selector: 'hero-card',
  standalone: true,
  imports: [RouterModule, AppButtonComponent, PillComponent],
  templateUrl: './hero-card.component.html',
  styleUrl: './hero-card.component.css',
})
export class HeroCardComponent {
  @Input() pill?: string;
  @Input() title = 'Ready to launch your next event?';
  @Input() subtitle =
    'Create listings, manage inventory, and sell tickets with a checkout that converts.';

  @Input() primaryLabel = 'Get started';
  @Input() primaryHref: any[] = ['/signup'];

  @Input() secondaryLabel = 'Browse events';
  @Input() secondaryHref: any[] = ['/events'];

  @Input() className = '';
}
