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
    const base = 'h-11 w-11 rounded-2xl border flex items-center justify-center';
    const tones: Record<Tone, string> = {
      accent: 'border-white/10 bg-gradient-to-br from-purple-500/20 to-fuchsia-500/20',
      neutral: 'border-white/10 bg-white/5',
    };
    return `${base} ${tones[this.tone]} ${this.className}`.trim();
  }
}
