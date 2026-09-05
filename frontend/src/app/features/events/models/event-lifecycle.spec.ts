import { lifecycleBadgeClass, lifecycleHint } from './event-lifecycle';
import { ALL_LIFECYCLE_STATES } from './event.types';

describe('event lifecycle presentation', () => {
  describe('lifecycleBadgeClass', () => {
    it('gives every state a distinct badge', () => {
      const classes = ALL_LIFECYCLE_STATES.map((state) => lifecycleBadgeClass(state));

      expect(new Set(classes).size).toBe(ALL_LIFECYCLE_STATES.length);
      classes.forEach((value) => expect(value).toContain('border'));
    });

    it('reads Paused as an in-between state rather than a failure', () => {
      expect(lifecycleBadgeClass('Paused')).toContain('sky');
      expect(lifecycleBadgeClass('Cancelled')).toContain('rose');
      expect(lifecycleBadgeClass('Published')).toContain('emerald');
    });
  });

  describe('lifecycleHint', () => {
    it('explains the states whose consequences are not obvious', () => {
      expect(lifecycleHint('Paused')).toContain('keep their place');
      expect(lifecycleHint('Cancelled')).toContain('reinstate');
      expect(lifecycleHint('Archived')).toContain('unarchive');
    });

    it('stays quiet for the states that speak for themselves', () => {
      expect(lifecycleHint('Draft')).toBeNull();
      expect(lifecycleHint('Published')).toBeNull();
    });
  });
});
