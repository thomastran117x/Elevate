import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'icon',
  standalone: true,
  imports: [],
  templateUrl: './icon.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './icon.component.css',
})
export class IconComponent {
  @Input() emoji?: string;
  @Input() className = 'text-lg';
}
