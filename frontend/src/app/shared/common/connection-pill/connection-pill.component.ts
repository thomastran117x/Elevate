import { Component, Input } from '@angular/core';

export type ConnectionPillState = 'connecting' | 'live' | 'reconnecting' | 'offline';

/**
 * Shows whether live updates are actually flowing.
 *
 * Under SSE this state existed but was never surfaced, so a silently dead stream looked
 * exactly like a quiet thread.
 */
@Component({
  selector: 'app-connection-pill',
  standalone: true,
  imports: [],
  templateUrl: './connection-pill.component.html',
})
export class ConnectionPillComponent {
  @Input() state: ConnectionPillState = 'connecting';

  private static readonly Labels: Record<ConnectionPillState, string> = {
    connecting: 'Connecting',
    live: 'Live',
    reconnecting: 'Reconnecting',
    offline: 'Offline',
  };

  private static readonly Tones: Record<ConnectionPillState, string> = {
    connecting: 'border-line bg-surface-sunken text-faint',
    live: 'border-accent/25 bg-accent/10 text-accent',
    reconnecting: 'border-warning/30 bg-warning/10 text-warning',
    offline: 'border-danger/30 bg-danger/10 text-danger',
  };

  private static readonly Dots: Record<ConnectionPillState, string> = {
    connecting: 'bg-faint',
    live: 'bg-accent motion-safe:animate-pulse',
    reconnecting: 'bg-warning motion-safe:animate-pulse',
    offline: 'bg-danger',
  };

  get label(): string {
    return ConnectionPillComponent.Labels[this.state];
  }

  get classes(): string {
    return ConnectionPillComponent.Tones[this.state];
  }

  get dotClasses(): string {
    return ConnectionPillComponent.Dots[this.state];
  }
}
