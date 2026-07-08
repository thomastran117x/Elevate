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
    const base = 'inline-flex items-center rounded-full px-3 py-1 text-xs border';
    const tones: Record<Tone, string> = {
      neutral: 'border-white/10 bg-white/5 text-white/70',
      accent: 'border-fuchsia-300/25 bg-fuchsia-500/10 text-fuchsia-100',
      soft: 'border-white/10 bg-slate-950/40 text-white/70',
      outline: 'border-white/15 bg-transparent text-white/70',
    };
    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
