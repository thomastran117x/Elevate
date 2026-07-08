import { Component, Input } from '@angular/core';

@Component({
  selector: 'container',
  standalone: true,
  imports: [],
  templateUrl: './container.component.html',
  styleUrl: './container.component.css',
})
export class ContainerComponent {
  @Input() className = '';
}
