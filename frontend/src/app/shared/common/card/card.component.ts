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
    const base = 'rounded-3xl border';
    const tones: Record<CardTone, string> = {
      glass: 'surface-panel',
      solid: 'surface-muted',
    };

    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
