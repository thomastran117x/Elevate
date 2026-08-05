import { detectBrowserTimeZone, formatLocalStart, listTimeZones } from './time-zones';

describe('detectBrowserTimeZone', () => {
  it('returns the runtime zone when it is a real IANA name', () => {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: 'America/Toronto' }),
    } as unknown as Intl.DateTimeFormat);

    expect(detectBrowserTimeZone()).toBe('America/Toronto');
  });

  it('falls back to UTC for a single-segment zone the API would reject', () => {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: 'GMT' }),
    } as unknown as Intl.DateTimeFormat);

    expect(detectBrowserTimeZone()).toBe('UTC');
  });

  it('falls back to UTC when the runtime reports no zone', () => {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: undefined }),
    } as unknown as Intl.DateTimeFormat);

    expect(detectBrowserTimeZone()).toBe('UTC');
  });

  it('falls back to UTC when the runtime throws', () => {
    spyOn(Intl, 'DateTimeFormat').and.throwError('unsupported');

    expect(detectBrowserTimeZone()).toBe('UTC');
  });
});

describe('listTimeZones', () => {
  it('always includes UTC and the browser zone, sorted and deduplicated', () => {
    const zones = listTimeZones();

    expect(zones).toContain('UTC');
    expect(zones).toContain(detectBrowserTimeZone());
    expect(new Set(zones).size).toBe(zones.length);
    expect([...zones].sort((a, b) => a.localeCompare(b))).toEqual(zones);
  });

  it('drops offset-style entries the API rejects', () => {
    const supported = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
    const original = supported.supportedValuesOf;
    supported.supportedValuesOf = () => ['+05:00', 'GMT', 'Europe/Paris', 'UTC'];

    try {
      const zones = listTimeZones();

      expect(zones).not.toContain('+05:00');
      expect(zones).not.toContain('GMT');
      expect(zones).toContain('Europe/Paris');
    } finally {
      supported.supportedValuesOf = original;
    }
  });

  it('uses the built-in fallback list when the runtime lacks supportedValuesOf', () => {
    const supported = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
    const original = supported.supportedValuesOf;
    delete supported.supportedValuesOf;

    try {
      const zones = listTimeZones();

      expect(zones).toContain('Europe/London');
      expect(zones).toContain('Australia/Sydney');
      expect(zones.length).toBeGreaterThan(10);
    } finally {
      supported.supportedValuesOf = original;
    }
  });
});

describe('formatLocalStart', () => {
  it('formats a wall-clock string without shifting it across zones', () => {
    // 3 March 2026 is a Tuesday. The time must come through as written, not re-zoned.
    expect(formatLocalStart('2026-03-03T19:00:00')).toBe('Tue 3 Mar 2026, 19:00');
  });

  it('truncates seconds from the time part', () => {
    expect(formatLocalStart('2026-03-03T19:00:45')).toContain('19:00');
    expect(formatLocalStart('2026-03-03T19:00:45')).not.toContain('19:00:45');
  });

  it('returns the input unchanged when the date part is unparseable', () => {
    expect(formatLocalStart('not-a-date')).toBe('not-a-date');
    expect(formatLocalStart('')).toBe('');
  });

  it('handles a missing time part', () => {
    expect(formatLocalStart('2026-03-03')).toBe('Tue 3 Mar 2026, ');
  });
});
