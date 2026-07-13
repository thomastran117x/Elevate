import { Routes } from '@angular/router';

import { featureCanMatch } from '../../core/features/feature-can-match.guard';
import { FEATURE_KEYS } from '../../core/features/feature-flags.types';
import { clubManageAuthGuard } from './guards/club-manage-auth.guard';
import { unsavedChangesGuard } from './guards/unsaved-changes.guard';

export const CLUBS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/clubs-search/clubs-search.component').then((m) => m.ClubsSearchComponent),
  },
  // Owner-side management. Literal `manage` segments must precede the `:clubId` catch-all.
  {
    path: 'manage/new',
    canActivate: [clubManageAuthGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./pages/manage/club-editor/club-editor.component').then((m) => m.ClubEditorComponent),
  },
  {
    path: 'manage',
    canActivate: [clubManageAuthGuard],
    loadComponent: () =>
      import('./pages/manage/managed-clubs/managed-clubs.component').then(
        (m) => m.ManagedClubsComponent,
      ),
  },
  {
    path: ':clubId/manage',
    canActivate: [clubManageAuthGuard],
    loadComponent: () =>
      import('./pages/manage/club-manage-shell/club-manage-shell.component').then(
        (m) => m.ClubManageShellComponent,
      ),
    loadChildren: () => import('./pages/manage/club-manage.routes').then((m) => m.CLUB_MANAGE_TABS),
  },
  {
    path: ':clubId/posts/:postId',
    canMatch: [featureCanMatch(FEATURE_KEYS.clubsPosts)],
    loadComponent: () =>
      import('./pages/post-detail/club-post-detail.component').then(
        (m) => m.ClubPostDetailComponent,
      ),
  },
  {
    path: ':clubId/posts',
    canMatch: [featureCanMatch(FEATURE_KEYS.clubsPosts)],
    loadComponent: () =>
      import('./pages/posts-list/club-posts.component').then((m) => m.ClubPostsComponent),
  },
  {
    path: ':clubId',
    loadComponent: () =>
      import('./pages/club-detail/club-detail.component').then((m) => m.ClubDetailComponent),
  },
];
