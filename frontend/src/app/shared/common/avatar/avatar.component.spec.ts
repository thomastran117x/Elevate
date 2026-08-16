import { AvatarComponent } from './avatar.component';

describe('AvatarComponent', () => {
  let component: AvatarComponent;

  beforeEach(() => {
    component = new AvatarComponent();
  });

  it('builds initials from the first two words', () => {
    component.name = 'Taylor Rider';
    expect(component.initials).toBe('TR');

    component.name = 'Taylor Quinn Rider';
    expect(component.initials).toBe('TQ');

    component.name = 'Taylor';
    expect(component.initials).toBe('T');
  });

  it('falls back to a placeholder when there is no name', () => {
    component.name = null;
    expect(component.initials).toBe('?');
    expect(component.label).toBe('Unknown user');

    component.name = '   ';
    expect(component.initials).toBe('?');
  });

  it('applies the requested size', () => {
    component.size = 'xs';
    expect(component.classes).toContain('h-6');

    component.size = 'lg';
    expect(component.classes).toContain('h-11');
  });

  it('adds an accent ring only when marked online', () => {
    expect(component.classes).not.toContain('ring-accent');

    component.online = true;
    expect(component.classes).toContain('ring-accent');
  });

  it('appends any caller supplied classes', () => {
    component.className = 'ring-2 ring-surface';
    expect(component.classes).toContain('ring-2 ring-surface');
  });
});
