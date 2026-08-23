import { EventLifecycleTransition, ManagedEvent } from '../../models/event.types';
import { EventLifecycleActionsComponent } from './lifecycle-actions.component';

function transition(overrides: Partial<EventLifecycleTransition> = {}): EventLifecycleTransition {
  return {
    key: 'pause',
    target: 'Paused',
    label: 'Pause event',
    title: 'Pause this event?',
    isReversible: true,
    reversibleNote: 'Reversible — resume any time.',
    isDestructive: false,
    impacts: ['It is removed from public search and listings.'],
    ...overrides,
  };
}

function makeEvent(overrides: Partial<ManagedEvent> = {}): ManagedEvent {
  return {
    id: 1,
    imageUrls: [],
    isPrivate: false,
    registerCost: 0,
    clubId: 3,
    currentVersionNumber: 1,
    createdAt: '2026-08-20T12:00:00Z',
    updatedAt: '2026-08-20T12:00:00Z',
    lifecycleState: 'Published',
    category: 'Other',
    tags: [],
    registrationCount: 0,
    waitlistEnabled: false,
    waitlistCount: 0,
    publishReady: true,
    publishIssues: [],
    availableTransitions: [transition()],
    ...overrides,
  } as ManagedEvent;
}

describe('EventLifecycleActionsComponent', () => {
  let component: EventLifecycleActionsComponent;
  let emitted: string[];

  beforeEach(() => {
    component = new EventLifecycleActionsComponent();
    component.event = makeEvent();
    emitted = [];
    component.act.subscribe((key) => emitted.push(key));
  });

  it('renders whatever the server advertises rather than a hardcoded set', () => {
    component.event = makeEvent({
      availableTransitions: [
        transition({ key: 'resume' }),
        transition({ key: 'cancel', isDestructive: true }),
      ],
    });

    expect(component.transitions.map((item) => item.key)).toEqual(['resume', 'cancel']);
  });

  it('shows nothing for a state with no moves left', () => {
    component.event = makeEvent({ availableTransitions: [] });
    expect(component.transitions).toEqual([]);
  });

  describe('confirmation gate', () => {
    it('does not act until the operator confirms', () => {
      component.request(transition());

      expect(component.pending).not.toBeNull();
      expect(emitted).toEqual([]);

      component.onResolve(true);
      expect(emitted).toEqual(['pause']);
      expect(component.pending).toBeNull();
    });

    it('drops the action when the operator backs out', () => {
      component.request(transition());
      component.onResolve(false);

      expect(emitted).toEqual([]);
      expect(component.pending).toBeNull();
    });

    it('ignores clicks while a request is already in flight', () => {
      component.busy = true;
      component.request(transition());

      expect(component.pending).toBeNull();
    });
  });

  describe('readiness', () => {
    it('blocks a move the server marked as blocked, and says why', () => {
      const publish = transition({
        key: 'publish',
        target: 'Published',
        blockedReason: 'Start time must be in the future.',
      });
      component.event = makeEvent({ availableTransitions: [publish] });

      expect(component.isBlocked(publish)).toBeTrue();
      expect(component.blockedReason(publish)).toBe(
        'Not ready yet: Start time must be in the future.',
      );

      component.request(publish);
      expect(component.pending).toBeNull();
    });

    it('allows a move the server left unblocked', () => {
      const publish = transition({ key: 'publish', target: 'Published', blockedReason: null });
      component.event = makeEvent({ availableTransitions: [publish] });

      expect(component.isBlocked(publish)).toBeFalse();
      expect(component.blockedReason(publish)).toBeNull();
    });

    it('does not gate on publishReady, which does not apply to every move to Published', () => {
      // Resuming or reinstating an event that has already started is legitimate; the old
      // publishReady check would have disabled exactly the recovery this feature offers.
      const resume = transition({ key: 'resume', target: 'Published', blockedReason: null });
      component.event = makeEvent({
        publishReady: false,
        publishIssues: ['Start time must be in the future.'],
        availableTransitions: [resume],
      });

      expect(component.isBlocked(resume)).toBeFalse();
    });
  });

  it('paints destructive moves in the danger tone', () => {
    expect(component.buttonClass(transition({ isDestructive: true }))).toContain('text-danger');
    expect(component.buttonClass(transition({ isDestructive: false }))).not.toContain(
      'text-danger',
    );
  });
});
