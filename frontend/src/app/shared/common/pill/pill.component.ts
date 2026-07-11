import { Component, Input } from '@angular/core';

type Tone = 'neutral' | 'accent' | 'soft' | 'outline';

@Component({
  selector: 'pill',
  standalone: true,
  imports: [],
  templateUrl: './pill.component.html',
  styleUrl: './pill.component.css',
})
export class PillComponent {
  @Input() tone: Tone = 'soft';
  @Input() className = '';

  get classes() {
    const base =
      'inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold tracking-[0.04em]';
    const tones: Record<Tone, string> = {
      neutral: 'border-line bg-surface text-muted',
      accent: 'border-accent/20 bg-accent/10 text-accent',
      soft: 'border-line bg-surface-muted text-muted',
      outline: 'border-line-strong bg-transparent text-muted',
    };

    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
