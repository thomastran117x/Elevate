import { Routes } from '@angular/router';

export const CLUBS_ROUTES: Routes = [
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
];
