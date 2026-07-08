import { Component, Input } from '@angular/core';

@Component({
  selector: 'inline-code',
  standalone: true,
  imports: [],
  templateUrl: './inline-code.component.html',
  styleUrl: './inline-code.component.css',
})
export class InlineCodeComponent {
  @Input() className = '';
}
