import { ConnectionPillComponent, ConnectionPillState } from './connection-pill.component';

describe('ConnectionPillComponent', () => {
  let component: ConnectionPillComponent;

  beforeEach(() => {
    component = new ConnectionPillComponent();
  });

  const cases: { state: ConnectionPillState; label: string; tone: string }[] = [
    { state: 'connecting', label: 'Connecting', tone: 'text-faint' },
    { state: 'live', label: 'Live', tone: 'text-accent' },
    { state: 'reconnecting', label: 'Reconnecting', tone: 'text-warning' },
    { state: 'offline', label: 'Offline', tone: 'text-danger' },
  ];

  for (const { state, label, tone } of cases) {
    it(`labels and tones the ${state} state`, () => {
      component.state = state;

      expect(component.label).toBe(label);
      expect(component.classes).toContain(tone);
      expect(component.dotClasses).not.toBe('');
    });
  }

  it('pulses the dot only while a connection is being maintained', () => {
    component.state = 'live';
    expect(component.dotClasses).toContain('animate-pulse');

    component.state = 'reconnecting';
    expect(component.dotClasses).toContain('animate-pulse');

    component.state = 'offline';
    expect(component.dotClasses).not.toContain('animate-pulse');
  });
});
