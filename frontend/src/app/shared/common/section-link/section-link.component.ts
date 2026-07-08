import { Component, Input } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'section-link',
  standalone: true,
  imports: [RouterModule],
  templateUrl: './section-link.component.html',
  styleUrl: './section-link.component.css',
})
export class SectionLinkComponent {
  @Input({ required: true }) href!: any[];
  @Input() className = '';
}
