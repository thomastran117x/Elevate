import { Component, Input } from '@angular/core';

export type DividerTone = 'subtle' | 'strong';

@Component({
  selector: 'divider',
  standalone: true,
  imports: [],
  templateUrl: './divider.component.html',
  styleUrl: './divider.component.css',
})
export class DividerComponent {
  @Input() tone: DividerTone = 'subtle';
  @Input() label?: string;
  @Input() className = 'my-6';

  get lineClass() {
    const base = 'h-px w-full';
    const tones: Record<DividerTone, string> = {
      subtle: 'bg-white/10',
      strong: 'bg-white/18',
    };
    return `${base} ${tones[this.tone]}`;
  }
}
