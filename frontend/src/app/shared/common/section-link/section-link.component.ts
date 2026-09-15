import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'section-link',
  standalone: true,
  imports: [RouterModule],
  templateUrl: './section-link.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './section-link.component.css',
})
export class SectionLinkComponent {
  @Input({ required: true }) href!: any[];
  @Input() className = '';
}
