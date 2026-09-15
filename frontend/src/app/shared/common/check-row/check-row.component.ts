import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'check-row',
  standalone: true,
  imports: [],
  templateUrl: './check-row.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './check-row.component.css',
})
export class CheckRowComponent {
  @Input() title?: string;
  @Input() className = '';
}
