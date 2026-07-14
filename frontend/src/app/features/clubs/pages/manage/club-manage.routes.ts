import { Routes } from '@angular/router';

import { unsavedChangesGuard } from '../../guards/unsaved-changes.guard';

export const CLUB_MANAGE_TABS: Routes = [
  { path: '', redirectTo: 'overview', pathMatch: 'full' },
  {
    path: 'overview',
    loadComponent: () =>
      import('./overview-tab/overview-tab.component').then((m) => m.OverviewTabComponent),
  },
  {
    path: 'members',
    loadComponent: () =>
      import('./members-tab/members-tab.component').then((m) => m.MembersTabComponent),
  },
  {
    path: 'posts',
    loadComponent: () => import('./posts-tab/posts-tab.component').then((m) => m.PostsTabComponent),
  },
  {
    path: 'events',
    loadComponent: () =>
      import('./events-tab/events-tab.component').then((m) => m.EventsTabComponent),
  },
  {
    path: 'details',
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./club-editor/club-editor.component').then((m) => m.ClubEditorComponent),
  },
  {
    path: 'staff',
    loadComponent: () => import('./staff-tab/staff-tab.component').then((m) => m.StaffTabComponent),
  },
  {
    path: 'history',
    loadComponent: () =>
      import('./history-tab/history-tab.component').then((m) => m.HistoryTabComponent),
  },
  {
    path: 'analytics',
    loadComponent: () =>
      import('./analytics-tab/analytics-tab.component').then((m) => m.AnalyticsTabComponent),
  },
  {
    path: 'settings',
    loadComponent: () =>
      import('./settings-tab/settings-tab.component').then((m) => m.SettingsTabComponent),
  },
];
