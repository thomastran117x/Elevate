import { CLIENT_LABELS, DEVICE_LABELS } from '../activity-tab/security-activity-labels';

/**
 * How a session or trusted device is named in the list, e.g. "Chrome · Desktop". Built only from
 * the closed classes the API reports, so nothing a client sent about itself is shown.
 */
export function deviceDescription(client: string, deviceClass: string): string {
  const clientLabel = CLIENT_LABELS[client];
  const deviceLabel = DEVICE_LABELS[deviceClass];
  if (clientLabel && deviceLabel) {
    return `${clientLabel} · ${deviceLabel}`;
  }
  return clientLabel ?? deviceLabel ?? 'Unknown device';
}
