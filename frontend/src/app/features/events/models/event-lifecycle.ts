import { EventLifecycleState } from './event.types';

/**
 * Pill classes for a lifecycle badge.
 *
 * Extracted because the same switch had been copied into the editor, the club events tab, and
 * the series page — three places a new state could be forgotten. Adding one here covers all of
 * them.
 */
export function lifecycleBadgeClass(lifecycleState: EventLifecycleState): string {
  switch (lifecycleState) {
    case 'Draft':
      return 'bg-amber-500/10 text-amber-700 dark:text-amber-300 border border-amber-500/20';
    case 'Published':
      return 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 border border-emerald-500/20';
    case 'Paused':
      return 'bg-sky-500/10 text-sky-700 dark:text-sky-300 border border-sky-500/20';
    case 'Cancelled':
      return 'bg-rose-500/10 text-rose-700 dark:text-rose-300 border border-rose-500/20';
    case 'Archived':
      return 'bg-slate-500/10 text-slate-700 dark:text-slate-300 border border-slate-500/20';
  }
}

/** One line explaining what a non-obvious state means for the people looking at the event. */
export function lifecycleHint(lifecycleState: EventLifecycleState): string | null {
  switch (lifecycleState) {
    case 'Paused':
      return 'Off sale and hidden from search. People who already registered keep their place.';
    case 'Cancelled':
      return 'Shown as cancelled and closed to new registrations. You can reinstate it.';
    case 'Archived':
      return 'Hidden from everyone except club managers. You can unarchive it.';
    default:
      return null;
  }
}
