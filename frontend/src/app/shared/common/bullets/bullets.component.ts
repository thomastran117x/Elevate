import { Component, Input } from '@angular/core';

@Component({
  selector: 'bullets',
  standalone: true,
  imports: [],
  templateUrl: './bullets.component.html',
  styleUrl: './bullets.component.css',
})
export class BulletsComponent {
  @Input({ required: true }) items!: string[];
  @Input() className = '';
}
