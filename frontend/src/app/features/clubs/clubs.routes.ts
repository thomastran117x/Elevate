import { Routes } from '@angular/router';

import { featureCanMatch } from '../../core/features/feature-can-match.guard';
import { FEATURE_KEYS } from '../../core/features/feature-flags.types';
import { authenticatedUserGuard } from '../../core/guards/authenticated-user.guard';
import { unsavedChangesGuard } from './guards/unsaved-changes.guard';

export const CLUBS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/clubs-search/clubs-search.component').then((m) => m.ClubsSearchComponent),
  },
  // Owner-side management. Literal `manage` segments must precede the `:clubId` catch-all.
  // The guard only checks that the visitor is signed in — per-club ownership and manage
  // permissions are enforced server-side and surfaced as inline errors on these pages.
  {
    path: 'manage/new',
    canActivate: [authenticatedUserGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./pages/manage/club-editor/club-editor.component').then((m) => m.ClubEditorComponent),
  },
  {
    path: 'manage',
    canActivate: [authenticatedUserGuard],
    loadComponent: () =>
      import('./pages/manage/managed-clubs/managed-clubs.component').then(
        (m) => m.ManagedClubsComponent,
      ),
  },
  {
    path: ':clubId/manage',
    canActivate: [authenticatedUserGuard],
    loadComponent: () =>
      import('./pages/manage/club-manage-shell/club-manage-shell.component').then(
        (m) => m.ClubManageShellComponent,
      ),
    loadChildren: () => import('./pages/manage/club-manage.routes').then((m) => m.CLUB_MANAGE_TABS),
  },
  // Recipient-facing invite accept pages. Must precede the `:clubId` catch-all.
  {
    path: 'invite',
    loadComponent: () =>
      import('./pages/invite/club-invite.component').then((m) => m.ClubInviteComponent),
  },
  {
    path: 'member-invite',
    loadComponent: () =>
      import('./pages/member-invite/club-member-invite.component').then(
        (m) => m.ClubMemberInviteComponent,
      ),
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
    path: ':clubId/discussions',
    canMatch: [featureCanMatch(FEATURE_KEYS.clubsDiscussions)],
    loadComponent: () =>
      import('./pages/discussions-list/club-discussions.component').then(
        (m) => m.ClubDiscussionsComponent,
      ),
  },
  {
    path: ':clubId',
    loadComponent: () =>
      import('./pages/club-detail/club-detail.component').then((m) => m.ClubDetailComponent),
  },
];
