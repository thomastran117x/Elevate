import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: 'login', redirectTo: 'auth/login', pathMatch: 'full' },
  { path: 'signup', redirectTo: 'auth/signup', pathMatch: 'full' },
  { path: 'register', redirectTo: 'auth/signup', pathMatch: 'full' },
  { path: 'forgot-password', redirectTo: 'auth/forgot-password', pathMatch: 'full' },
  { path: 'change-password', redirectTo: 'auth/change-password', pathMatch: 'full' },
  {
    path: '',
    loadChildren: () => import('./features/main/main.module').then((m) => m.MainModule),
  },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.module').then((m) => m.AuthModule),
  },
  {
    path: 'events',
    loadChildren: () => import('./features/events/events.routes').then((m) => m.EVENTS_ROUTES),
  },
];
