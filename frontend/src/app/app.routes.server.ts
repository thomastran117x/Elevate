import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // Authenticated / per-user pages must render on the client. The server has no
  // session cookie, and their data comes from authenticated backend calls that
  // should run in — and be inspectable from — the browser (not the SSR process).
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'account/**', renderMode: RenderMode.Client },
  { path: 'profile/**', renderMode: RenderMode.Client },

  // Everything else (public/marketing pages) keeps server-side rendering.
  { path: '**', renderMode: RenderMode.Server },
];
