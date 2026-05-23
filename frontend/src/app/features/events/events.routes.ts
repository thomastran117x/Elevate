import { Routes } from '@angular/router';

export const EVENTS_ROUTES: Routes = [
  {
    path: 'invite',
    loadComponent: () =>
      import('./pages/invite/event-invite.component').then((m) => m.EventInviteComponent),
  },
  {
    path: 'me/invited',
    loadComponent: () =>
      import('./pages/my-invites/my-invites.component').then((m) => m.MyInvitesComponent),
  },
  {
    path: ':eventId/invitations/manage',
    loadComponent: () =>
      import('./pages/manage-invitations/manage-event-invitations.component').then(
        (m) => m.ManageEventInvitationsComponent,
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
