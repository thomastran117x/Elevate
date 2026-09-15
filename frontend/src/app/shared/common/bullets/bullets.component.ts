import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'bullets',
  standalone: true,
  imports: [],
  templateUrl: './bullets.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './bullets.component.css',
})
export class BulletsComponent {
  @Input({ required: true }) items!: string[];
  @Input() className = '';
}
