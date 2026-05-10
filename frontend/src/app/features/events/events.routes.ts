import { Routes } from '@angular/router';

export const EVENTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/search/events-search.component').then(
        (m) => m.EventsSearchComponent,
      ),
  },
];
