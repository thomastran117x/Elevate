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
    const base = 'rounded-3xl border border-line shadow-2xl';
    const tones: Record<CardTone, string> = {
      glass: 'bg-glass backdrop-blur shadow-purple-500/10 hover:border-line-strong transition',
      solid: 'bg-surface-sunken shadow-purple-500/10',
    };
    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
