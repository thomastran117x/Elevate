import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

export type TimelineItem = {
  title: string;
  desc?: string;
  meta?: string;
};

@Component({
  selector: 'timeline',
  standalone: true,
  imports: [],
  templateUrl: './timeline.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './timeline.component.css',
})
export class TimelineComponent {
  @Input({ required: true }) items!: TimelineItem[];
  @Input() className = '';
}
