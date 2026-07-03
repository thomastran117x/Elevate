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
  },
  {
    path: 'signup',
    redirectTo: 'auth/signup',
    pathMatch: 'full',
  },
  {
    path: 'register',
    redirectTo: 'auth/signup',
    pathMatch: 'full',
  },
  {
    path: 'forgot-password',
    redirectTo: 'auth/forgot-password',
    pathMatch: 'full',
  },
  {
    path: 'change-password',
    redirectTo: 'auth/change-password',
    pathMatch: 'full',
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
    redirectTo: '/account/security',
    pathMatch: 'full',
  },
  {
    path: 'account',
    canMatch: [featureCanMatch(FEATURE_KEYS.profile)],
    canActivate: [authenticatedUserGuard],
    loadComponent: () =>
      import('./features/profile/pages/account/account-page.component').then(
        (m) => m.AccountPageComponent,
      ),
    loadChildren: () =>
      import('./features/profile/pages/account/account.routes').then((m) => m.ACCOUNT_ROUTES),
  },
  {
    path: 'profile/:username',
    canMatch: [featureCanMatch(FEATURE_KEYS.profile)],
    loadComponent: () =>
      import('./features/profile/pages/public-profile/public-profile.component').then(
        (m) => m.PublicProfileComponent,
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
