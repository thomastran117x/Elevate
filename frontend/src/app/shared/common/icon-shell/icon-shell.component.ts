import { Component, Input } from '@angular/core';

type Tone = 'accent' | 'neutral';

@Component({
  selector: 'icon-shell',
  standalone: true,
  imports: [],
  templateUrl: './icon-shell.component.html',
  styleUrl: './icon-shell.component.css',
})
export class IconShellComponent {
  @Input() tone: Tone = 'accent';
  @Input() className = '';

  get classes() {
    const base = 'flex h-11 w-11 items-center justify-center rounded-2xl border';
    const tones: Record<Tone, string> = {
      accent: 'border-accent/20 bg-accent/10 text-accent',
      neutral: 'border-line bg-surface-muted text-muted',
    };

    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
