import { Routes } from '@angular/router';

import { featureCanMatch } from '../../core/features/feature-can-match.guard';
import { FEATURE_KEYS } from '../../core/features/feature-flags.types';
import { eventsManageAuthGuard } from './guards/events-manage-auth.guard';

export const EVENTS_ROUTES: Routes = [
  {
    path: 'clubs/:clubId/manage/new',
    canActivate: [eventsManageAuthGuard],
    loadComponent: () =>
      import('./pages/manage-editor/manage-event-editor.component').then(
        (m) => m.ManageEventEditorComponent,
      ),
  },
  {
    path: 'clubs/:clubId/manage',
    canActivate: [eventsManageAuthGuard],
    loadComponent: () =>
      import('./pages/manage-list/manage-club-events.component').then(
        (m) => m.ManageClubEventsComponent,
      ),
  },
  {
    path: 'invite',
    canMatch: [featureCanMatch(FEATURE_KEYS.eventsInvitations)],
    loadComponent: () =>
      import('./pages/invite/event-invite.component').then((m) => m.EventInviteComponent),
  },
  {
    path: 'me/invited',
    canMatch: [featureCanMatch(FEATURE_KEYS.eventsInvitations)],
    loadComponent: () =>
      import('./pages/my-invites/my-invites.component').then((m) => m.MyInvitesComponent),
  },
  {
    path: ':eventId/invitations/manage',
    canActivate: [eventsManageAuthGuard],
    canMatch: [featureCanMatch(FEATURE_KEYS.eventsInvitations)],
    loadComponent: () =>
      import('./pages/manage-invitations/manage-event-invitations.component').then(
        (m) => m.ManageEventInvitationsComponent,
      ),
  },
  {
    path: ':eventId/manage',
    canActivate: [eventsManageAuthGuard],
    loadComponent: () =>
      import('./pages/manage-editor/manage-event-editor.component').then(
        (m) => m.ManageEventEditorComponent,
      ),
  },
  {
    path: ':eventId',
    loadComponent: () =>
      import('./pages/detail/event-detail.component').then((m) => m.EventDetailComponent),
  },
  {
    path: '',
    loadComponent: () =>
      import('./pages/search/events-search.component').then((m) => m.EventsSearchComponent),
    pathMatch: 'full',
  },
];
