import { CanDeactivateFn } from '@angular/router';

/**
 * Components that hold unsaved edits implement this to gate navigation away.
 * Returning false (or a confirm that resolves false) keeps the user on the page.
 */
export interface CanComponentDeactivate {
  canDeactivate: () => boolean;
}

export const unsavedChangesGuard: CanDeactivateFn<CanComponentDeactivate> = (component) => {
  return component.canDeactivate ? component.canDeactivate() : true;
};
