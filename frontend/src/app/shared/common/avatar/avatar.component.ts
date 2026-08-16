import { Component, Input } from '@angular/core';

export type AvatarSize = 'xs' | 'sm' | 'md' | 'lg';

/**
 * User avatar with an initials fallback.
 *
 * Replaces the ad-hoc `h-8 w-8 rounded-full` blocks that were duplicated across the
 * discussion and comment templates.
 */
@Component({
  selector: 'app-avatar',
  standalone: true,
  imports: [],
  templateUrl: './avatar.component.html',
})
export class AvatarComponent {
  @Input() src: string | null = null;
  @Input() name: string | null = null;
  @Input() size: AvatarSize = 'md';
  /** Renders a small accent ring, used to mark someone as currently online. */
  @Input() online = false;
  @Input() className = '';

  private static readonly Sizes: Record<AvatarSize, string> = {
    xs: 'h-6 w-6 text-[10px]',
    sm: 'h-8 w-8 text-xs',
    md: 'h-9 w-9 text-xs',
    lg: 'h-11 w-11 text-sm',
  };

  get classes(): string {
    const ring = this.online ? 'ring-2 ring-accent ring-offset-2 ring-offset-surface' : '';
    return `${AvatarComponent.Sizes[this.size]} ${ring} ${this.className}`.trim();
  }

  /** First letters of the first two words, e.g. "Taylor Rider" → "TR". */
  get initials(): string {
    const source = this.name?.trim();
    if (!source) return '?';
    return source
      .split(/\s+/)
      .slice(0, 2)
      .map((word) => word.charAt(0).toUpperCase())
      .join('');
  }

  get label(): string {
    return this.name ?? 'Unknown user';
  }
}
