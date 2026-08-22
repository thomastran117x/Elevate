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

  describe('publish readiness', () => {
    const publish = transition({ key: 'publish', target: 'Published', label: 'Publish event' });

    it('blocks moves to Published while the event still has issues', () => {
      component.event = makeEvent({
        publishReady: false,
        publishIssues: ['Start time must be in the future.'],
        availableTransitions: [publish],
      });

      expect(component.isBlocked(publish)).toBeTrue();
      expect(component.blockedReason(publish)).toBe(
        'Not ready yet: Start time must be in the future.',
      );

      component.request(publish);
      expect(component.pending).toBeNull();
    });

    it('allows the move once the event is ready', () => {
      component.event = makeEvent({ publishReady: true, availableTransitions: [publish] });

      expect(component.isBlocked(publish)).toBeFalse();
      expect(component.blockedReason(publish)).toBeNull();
    });

    it('does not block moves that are not going live', () => {
      component.event = makeEvent({ publishReady: false, publishIssues: ['Anything'] });

      expect(component.isBlocked(transition({ key: 'archive', target: 'Archived' }))).toBeFalse();
    });
  });

  it('paints destructive moves in the danger tone', () => {
    expect(component.buttonClass(transition({ isDestructive: true }))).toContain('text-danger');
    expect(component.buttonClass(transition({ isDestructive: false }))).not.toContain(
      'text-danger',
    );
  });
});
