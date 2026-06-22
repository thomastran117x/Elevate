import { Routes } from '@angular/router';

import { featureCanMatch } from '../../core/features/feature-can-match.guard';
import { FEATURE_KEYS } from '../../core/features/feature-flags.types';

export const CLUBS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/clubs-search/clubs-search.component').then((m) => m.ClubsSearchComponent),
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
