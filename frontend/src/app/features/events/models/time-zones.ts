/**
 * IANA time zone options for the recurrence rule builder.
 *
 * The backend stores an IANA identifier on the series and anchors occurrences to local
 * wall-clock time in that zone, so this must always yield IANA names — never a UTC offset and
 * never a Windows zone name, both of which the API rejects.
 */

/** Fallback for browsers (and SSR runtimes) without `Intl.supportedValuesOf`. */
const FALLBACK_TIME_ZONES: readonly string[] = [
  'UTC',
  'Africa/Cairo',
  'Africa/Johannesburg',
  'Africa/Lagos',
  'Africa/Nairobi',
  'America/Anchorage',
  'America/Argentina/Buenos_Aires',
  'America/Bogota',
  'America/Chicago',
  'America/Denver',
  'America/Halifax',
  'America/Los_Angeles',
  'America/Mexico_City',
  'America/New_York',
  'America/Phoenix',
  'America/Sao_Paulo',
  'America/Toronto',
  'America/Vancouver',
  'Asia/Bangkok',
  'Asia/Dubai',
  'Asia/Hong_Kong',
  'Asia/Jakarta',
  'Asia/Jerusalem',
  'Asia/Kolkata',
  'Asia/Manila',
  'Asia/Seoul',
  'Asia/Shanghai',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Atlantic/Reykjavik',
  'Australia/Adelaide',
  'Australia/Brisbane',
  'Australia/Melbourne',
  'Australia/Perth',
  'Australia/Sydney',
  'Europe/Amsterdam',
  'Europe/Athens',
  'Europe/Berlin',
  'Europe/Dublin',
  'Europe/Istanbul',
  'Europe/Lisbon',
  'Europe/London',
  'Europe/Madrid',
  'Europe/Moscow',
  'Europe/Paris',
  'Europe/Prague',
  'Europe/Rome',
  'Europe/Stockholm',
  'Europe/Warsaw',
  'Europe/Zurich',
  'Pacific/Auckland',
  'Pacific/Fiji',
  'Pacific/Honolulu',
];

/** The viewer's own zone, or UTC when the runtime cannot report one. */
export function detectBrowserTimeZone(): string {
  try {
    const resolved = Intl.DateTimeFormat().resolvedOptions().timeZone;
    return resolved && resolved.includes('/') ? resolved : 'UTC';
  } catch {
    return 'UTC';
  }
}

/**
 * Every IANA zone the runtime knows about, alphabetically. Offset-style entries and
 * single-segment aliases are filtered out so only values the API accepts can be chosen.
 */
export function listTimeZones(): string[] {
  const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] })
    .supportedValuesOf;

  const zones =
    typeof supported === 'function' ? supported.call(Intl, 'timeZone') : [...FALLBACK_TIME_ZONES];

  const usable = zones.filter((zone) => zone.includes('/') || zone === 'UTC');
  const withBrowserZone = new Set<string>([detectBrowserTimeZone(), 'UTC', ...usable]);

  return [...withBrowserZone].sort((a, b) => a.localeCompare(b));
}

/** Formats a wall-clock string (`2026-03-03T19:00:00`) for display, without re-zoning it. */
export function formatLocalStart(localStart: string): string {
  const [datePart, timePart = ''] = localStart.split('T');
  const [year, month, day] = datePart.split('-').map(Number);

  if (!year || !month || !day) {
    return localStart;
  }

  // Build the date in the *browser's* zone purely to borrow month and weekday names. The
  // numbers come straight from the string, so nothing is shifted across a zone boundary.
  const forNames = new Date(year, month - 1, day);
  const weekday = forNames.toLocaleDateString(undefined, { weekday: 'short' });
  const monthName = forNames.toLocaleDateString(undefined, { month: 'short' });

  return `${weekday} ${day} ${monthName} ${year}, ${timePart.slice(0, 5)}`;
}
