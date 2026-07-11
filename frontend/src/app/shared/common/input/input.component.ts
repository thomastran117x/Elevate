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
    const base =
      'form-field focus-ring flex items-center gap-2 rounded-xl border px-3 py-2.5 transition';
    const tone = this.tone === 'glass' ? 'bg-surface-muted' : 'bg-surface';
    const err = this.error ? 'border-danger/30 ring-4 ring-danger/10' : '';
    const dis = this.disabled ? 'opacity-60 cursor-not-allowed' : '';

    return `${base} ${tone} ${err} ${dis}`.trim();
  }

  get inputClass() {
    return (
      'w-full bg-transparent outline-none text-sm text-content placeholder:text-faint ' +
      'disabled:cursor-not-allowed'
    );
  }
}
