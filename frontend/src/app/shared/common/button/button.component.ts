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
      'inline-flex items-center justify-center gap-2 rounded-xl font-semibold transition duration-200 ' +
      'focus:outline-none focus-visible:ring-4 focus-visible:ring-accent/15 active:translate-y-px';

    const sizes: Record<AppButtonSize, string> = {
      sm: 'px-3 py-2 text-xs',
      md: 'px-4 py-2.5 text-sm',
      lg: 'px-5 py-3 text-sm',
    };

    const variants: Record<AppButtonVariant, string> = {
      primary: 'cta-solid',
      secondary: 'cta-subtle',
      ghost: 'cta-ghost',
      danger: 'border border-danger/25 bg-danger/10 text-danger hover:bg-danger/15',
    };

    const disabled = this.disabled ? 'opacity-60 cursor-not-allowed pointer-events-none' : '';

    return `${base} ${sizes[this.size]} ${variants[this.variant]} ${disabled} ${this.className}`.trim();
  }
}
