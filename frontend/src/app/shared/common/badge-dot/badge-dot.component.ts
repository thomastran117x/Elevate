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
      neutral: 'bg-white/40',
      success: 'bg-emerald-400',
      warning: 'bg-amber-400',
      danger: 'bg-red-400',
      accent: 'bg-fuchsia-400',
    };
    return `${base} ${tones[this.tone]}`;
  }
}
