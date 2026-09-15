import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'section',
  standalone: true,
  imports: [],
  templateUrl: './section.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './section.component.css',
})
export class SectionComponent {
  @Input() title?: string;
  @Input() subtitle?: string;

  @Input() className = '';
  @Input() contentClass = 'mt-6';
}
