import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { CanComponentDeactivate, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  const snapshots = [
    {} as ActivatedRouteSnapshot,
    {} as RouterStateSnapshot,
    {} as RouterStateSnapshot,
  ] as const;

  function run(component: CanComponentDeactivate) {
    return unsavedChangesGuard(component, ...snapshots);
  }

  it('allows navigation when the component reports no unsaved edits', () => {
    expect(run({ canDeactivate: () => true })).toBeTrue();
  });

  it('blocks navigation when the component reports unsaved edits', () => {
    expect(run({ canDeactivate: () => false })).toBeFalse();
  });

  it('allows navigation for a component that does not implement the hook', () => {
    expect(run({} as CanComponentDeactivate)).toBeTrue();
  });
});
