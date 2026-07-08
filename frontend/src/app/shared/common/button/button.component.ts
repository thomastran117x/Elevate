import { Component, EventEmitter, Input, Output } from '@angular/core';
import { RouterModule } from '@angular/router';

export type AppButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type AppButtonSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-button',
  standalone: true,
  imports: [RouterModule],
  templateUrl: './button.component.html',
  styleUrl: './button.component.css',
})
export class AppButtonComponent {
  @Input() variant: AppButtonVariant = 'primary';
  @Input() size: AppButtonSize = 'md';
  @Input() disabled = false;
  @Input() href?: any[];
  @Input() className = '';

  @Output() clicked = new EventEmitter<void>();

  get classes() {
    const base =
      'inline-flex items-center justify-center gap-2 rounded-xl font-semibold transition ' +
      'focus:outline-none focus-visible:ring-2 focus-visible:ring-white/25 ' +
      'active:translate-y-[0.5px]';

    const sizes: Record<AppButtonSize, string> = {
      sm: 'px-3 py-1.5 text-xs',
      md: 'px-4 py-2 text-sm',
      lg: 'px-5 py-2.5 text-sm',
    };

    const variants: Record<AppButtonVariant, string> = {
      primary:
        'bg-gradient-to-r from-purple-500 to-fuchsia-500 text-white ' +
        'shadow-lg shadow-purple-500/25 hover:opacity-95',
      secondary: 'bg-white text-slate-950 hover:bg-white/90 shadow-lg shadow-white/10',
      ghost: 'border border-white/15 bg-white/5 text-white/90 hover:bg-white/10',
      danger: 'border border-red-300/25 bg-red-500/10 text-red-100 hover:bg-red-500/15',
    };

    const disabled = this.disabled ? 'opacity-60 cursor-not-allowed pointer-events-none' : '';

    return `${base} ${sizes[this.size]} ${variants[this.variant]} ${disabled} ${this.className}`.trim();
  }
}
