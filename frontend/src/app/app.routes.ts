import { Routes } from '@angular/router';

import { featureCanMatch } from './core/features/feature-can-match.guard';
import { FEATURE_KEYS } from './core/features/feature-flags.types';
import { NotFoundComponent } from './shared/not-found/not-found.component';
import { authenticatedUserGuard } from './features/auth/guards/authenticated-user.guard';

export const routes: Routes = [
  {
    path: 'login',
    redirectTo: 'auth/login',
    pathMatch: 'full',
    canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
  },
  {
    path: 'signup',
    redirectTo: 'auth/signup',
    pathMatch: 'full',
    canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
  },
  {
    path: 'register',
    redirectTo: 'auth/signup',
    pathMatch: 'full',
    canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
  },
  {
    path: 'forgot-password',
    redirectTo: 'auth/forgot-password',
    pathMatch: 'full',
    canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
  },
  {
    path: 'change-password',
    redirectTo: 'auth/change-password',
    pathMatch: 'full',
    canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
  },
  {
    path: '',
    loadChildren: () => import('./features/main/main.module').then((m) => m.MainModule),
  },
  {
    path: 'auth',
    canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
    loadChildren: () => import('./features/auth/auth.module').then((m) => m.AuthModule),
  },

{
  path: 'settings/security',
  canMatch: [featureCanMatch(FEATURE_KEYS.auth)],
  canActivate: [authenticatedUserGuard],
  loadComponent: () =>
    import('./features/auth/pages/security-settings/security-settings.component').then(
      (m) => m.SecuritySettingsComponent,
    ),
},
  {
    path: 'events',
    canMatch: [featureCanMatch(FEATURE_KEYS.events)],
    loadChildren: () => import('./features/events/events.routes').then((m) => m.EVENTS_ROUTES),
  },
  {
    path: 'clubs',
    canMatch: [featureCanMatch(FEATURE_KEYS.clubs)],
    loadChildren: () => import('./features/clubs/clubs.routes').then((m) => m.CLUBS_ROUTES),
  },
  {
    path: '**',
    component: NotFoundComponent,
  },
];
