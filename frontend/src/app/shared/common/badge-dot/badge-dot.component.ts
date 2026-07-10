import { Component, Input } from '@angular/core';

type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent';

@Component({
  selector: 'badge-dot',
  standalone: true,
  imports: [],
  templateUrl: './badge-dot.component.html',
  styleUrl: './badge-dot.component.css',
})
export class BadgeDotComponent {
  @Input() tone: Tone = 'accent';
  @Input() className = '';

  get dotClass() {
    const base = 'h-2 w-2 rounded-full';
    const tones: Record<Tone, string> = {
      neutral: 'bg-surface-raised/40',
      success: 'bg-success',
      warning: 'bg-warning',
      danger: 'bg-danger',
      accent: 'bg-accent',
    };
    return `${base} ${tones[this.tone]}`;
  }
}
