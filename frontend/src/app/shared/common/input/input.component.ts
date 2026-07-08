import { Component, Input } from '@angular/core';

export type InputTone = 'default' | 'glass';

@Component({
  selector: 'app-input',
  standalone: true,
  imports: [],
  templateUrl: './input.component.html',
  styleUrl: './input.component.css',
})
export class AppInputComponent {
  @Input() label?: string;

  @Input() type: string = 'text';
  @Input() placeholder?: string;
  @Input() autocomplete?: string;
  @Input() inputmode?: string;
  @Input() disabled = false;

  @Input() prefix?: string;
  @Input() suffix?: string;

  @Input() hint?: string;
  @Input() error?: string;

  @Input() tone: InputTone = 'glass';
  @Input() className = '';

  get wrapperClass() {
    return `grid gap-2 ${this.className}`.trim();
  }

  get fieldWrapClass() {
    const base = 'flex items-center gap-2 rounded-xl border px-3 py-2 transition';
    const tone =
      this.tone === 'glass'
        ? 'bg-slate-950/50 border-white/10 focus-within:border-fuchsia-300/25 focus-within:ring-2 focus-within:ring-fuchsia-500/15'
        : 'bg-slate-950 border-white/10 focus-within:border-fuchsia-300/25 focus-within:ring-2 focus-within:ring-fuchsia-500/15';

    const err = this.error ? 'border-red-300/25 ring-2 ring-red-500/10' : '';
    const dis = this.disabled ? 'opacity-60 cursor-not-allowed' : '';

    return `${base} ${tone} ${err} ${dis}`.trim();
  }

  get inputClass() {
    return (
      'w-full bg-transparent outline-none text-sm text-white placeholder:text-white/40 ' +
      'disabled:cursor-not-allowed'
    );
  }
}
