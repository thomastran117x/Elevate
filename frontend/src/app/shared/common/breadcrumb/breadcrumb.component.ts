import { Component, Input } from '@angular/core';
import { RouterModule } from '@angular/router';

export type Crumb = { label: string; href?: any[] };

@Component({
  selector: 'breadcrumb',
  standalone: true,
  imports: [RouterModule],
  templateUrl: './breadcrumb.component.html',
  styleUrl: './breadcrumb.component.css',
})
export class BreadcrumbComponent {
  @Input({ required: true }) items!: Crumb[];
  @Input() className = '';
}
