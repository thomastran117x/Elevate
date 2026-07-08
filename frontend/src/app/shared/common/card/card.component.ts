import { Component, Input } from '@angular/core';

export type CardTone = 'glass' | 'solid';

@Component({
  selector: 'card',
  standalone: true,
  imports: [],
  templateUrl: './card.component.html',
  styleUrl: './card.component.css',
})
export class CardComponent {
  @Input() tone: CardTone = 'glass';
  @Input() className = '';

  get classes() {
    const base = 'rounded-3xl border border-white/10 shadow-2xl';
    const tones: Record<CardTone, string> = {
      glass: 'bg-white/5 backdrop-blur shadow-purple-500/10 hover:border-white/15 transition',
      solid: 'bg-slate-950/50 shadow-purple-500/10',
    };
    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
