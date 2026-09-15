import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'inline-code',
  standalone: true,
  imports: [],
  templateUrl: './inline-code.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './inline-code.component.css',
})
export class InlineCodeComponent {
  @Input() className = '';
}
