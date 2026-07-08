import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="min-h-[60vh] bg-slate-950 px-6 py-24 text-white">
      <div
        class="mx-auto max-w-2xl rounded-3xl border border-white/10 bg-white/5 p-10 text-center backdrop-blur"
      >
        <p class="text-xs uppercase tracking-[0.28em] text-white/45">404</p>
        <h1 class="mt-4 text-4xl font-semibold">This page is not available.</h1>
        <p class="mt-3 text-sm text-white/65">
          The route you requested does not exist or the feature is currently disabled.
        </p>
        <a
          routerLink="/"
          class="mt-8 inline-flex items-center justify-center rounded-xl bg-white px-5 py-3 text-sm font-semibold text-slate-950 transition hover:bg-white/90"
        >
          Back home
        </a>
      </div>
    </section>
  `,
})
export class NotFoundComponent {}
