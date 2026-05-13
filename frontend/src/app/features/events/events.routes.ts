import { Routes } from '@angular/router';

export const EVENTS_ROUTES: Routes = [
  {
    path: ':eventId',
    loadComponent: () =>
      import('./pages/detail/event-detail.component').then(
        (m) => m.EventDetailComponent,
      ),
  },
  {
    path: '',
    loadComponent: () =>
      import('./pages/search/events-search.component').then(
        (m) => m.EventsSearchComponent,
      ),
    pathMatch: 'full',
  },
];
