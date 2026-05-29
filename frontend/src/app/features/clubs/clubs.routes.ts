import { Routes } from '@angular/router';

export const CLUBS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/clubs-search/clubs-search.component').then((m) => m.ClubsSearchComponent),
  },
  {
    path: ':clubId/posts/:postId',
    loadComponent: () =>
      import('./pages/post-detail/club-post-detail.component').then(
        (m) => m.ClubPostDetailComponent,
      ),
  },
  {
    path: ':clubId/posts',
    loadComponent: () =>
      import('./pages/posts-list/club-posts.component').then((m) => m.ClubPostsComponent),
  },
  {
    path: ':clubId',
    loadComponent: () =>
      import('./pages/club-detail/club-detail.component').then((m) => m.ClubDetailComponent),
  },
];
