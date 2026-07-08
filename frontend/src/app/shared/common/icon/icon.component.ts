import { Component, Input } from '@angular/core';

@Component({
  selector: 'icon',
  standalone: true,
  imports: [],
  templateUrl: './icon.component.html',
  styleUrl: './icon.component.css',
})
export class IconComponent {
  @Input() emoji?: string;
  @Input() className = 'text-lg';
}
