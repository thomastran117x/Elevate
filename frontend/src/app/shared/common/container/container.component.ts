import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'container',
  standalone: true,
  imports: [],
  templateUrl: './container.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './container.component.css',
})
export class ContainerComponent {
  @Input() className = '';
}
