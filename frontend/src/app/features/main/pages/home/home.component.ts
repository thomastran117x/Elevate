import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';

import { FeatureFlagsService } from '../../../../core/features/feature-flags.service';
import { FEATURE_KEYS } from '../../../../core/features/feature-flags.types';

type Category = { name: string; icon: string; count: string };
type Feature = { title: string; desc: string; icon: string };
type EventCard = {
  title: string;
  venue: string;
  city: string;
  date: string;
  price: string;
  badge?: string;
};
type Testimonial = { quote: string; name: string; role: string };

@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [FormsModule, RouterModule],
  template: `
    <div class="min-h-screen bg-page text-content overflow-x-hidden overflow-y-visible relative">
      <div class="pointer-events-none absolute inset-0">
        <div
          class="absolute -top-24 left-1/2 -translate-x-1/2 h-[520px] w-[920px] rounded-full
                 bg-gradient-to-r from-purple-600/12 dark:from-purple-600/35 via-fuchsia-500/9 dark:via-fuchsia-500/25 to-indigo-500/9 dark:to-indigo-500/25 blur-3xl"
        ></div>
        <div
          class="absolute top-[520px] -left-20 h-[420px] w-[420px] rounded-full
                 bg-gradient-to-br from-indigo-500/7 dark:from-indigo-500/20 to-purple-500/4 dark:to-purple-500/10 blur-3xl"
        ></div>
        <div
          class="absolute top-[640px] -right-20 h-[420px] w-[420px] rounded-full
                 bg-gradient-to-br from-fuchsia-500/7 dark:from-fuchsia-500/20 to-purple-500/4 dark:to-purple-500/10 blur-3xl"
        ></div>
        <div
          class="absolute inset-0 opacity-[0.10]"
          style="
            background-image: linear-gradient(to right, rgba(255,255,255,0.08) 1px, transparent 1px),
                              linear-gradient(to bottom, rgba(255,255,255,0.08) 1px, transparent 1px);
            background-size: 56px 56px;
          "
        ></div>
      </div>

      <header class="relative z-10">
        <div class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div class="flex h-16 items-center justify-between">
            <a routerLink="/" class="flex items-center gap-2">
              <span
                class="inline-flex h-9 w-9 items-center justify-center rounded-xl
                       bg-gradient-to-br from-purple-500 to-fuchsia-500 shadow-lg shadow-purple-500/25"
              >
                <span class="text-base font-black">E</span>
              </span>
              <span class="text-sm font-semibold tracking-wide text-content">
                EventXperience
              </span>
            </a>

            <nav class="hidden md:flex items-center gap-7 text-sm text-muted">
              @if (eventsEnabled) {
                <a class="hover:text-content transition" routerLink="/events">Explore</a>
              }
              <a class="hover:text-content transition" routerLink="/venues">Venues</a>
              <a class="hover:text-content transition" routerLink="/pricing">Pricing</a>
              <a class="hover:text-content transition" routerLink="/contact">Contact</a>
            </nav>

            @if (authEnabled) {
              <div class="flex items-center gap-3">
                <a
                  routerLink="/auth/login"
                  class="hidden sm:inline-flex items-center rounded-xl px-3 py-2 text-sm
                       text-muted hover:text-content transition"
                >
                  Sign in
                </a>
                <a
                  routerLink="/auth/signup"
                  class="inline-flex items-center rounded-xl px-4 py-2 text-sm font-semibold
                       bg-inverse text-inverse-content hover:opacity-90 transition
                       shadow-lg shadow-white/10"
                >
                  Get started
                </a>
              </div>
            }
          </div>
        </div>
      </header>

      <main class="relative z-10">
        <section class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 pt-10 sm:pt-14 lg:pt-16">
          <div class="grid lg:grid-cols-12 gap-10 items-center">
            <div class="lg:col-span-7">
              <div
                class="inline-flex items-center gap-2 rounded-full border border-line bg-glass
                       px-3 py-1 text-xs text-muted backdrop-blur"
              >
                <span class="h-2 w-2 rounded-full bg-fuchsia-400"></span>
                Live inventory &bull; Verified tickets &bull; Instant delivery
              </div>

              <h1 class="mt-5 text-4xl sm:text-5xl lg:text-6xl font-extrabold tracking-tight">
                Book unforgettable events -
                <span class="text-gradient-accent">
                  fast, safe, and beautiful
                </span>
              </h1>

              <p class="mt-4 text-base sm:text-lg text-muted leading-relaxed max-w-2xl">
                Discover concerts, sports, theatre, and campus events. Real-time seating,
                transparent fees, and one-tap checkout. Built for modern organizers and fans.
              </p>

              @if (eventsEnabled) {
                <div
                  class="mt-7 rounded-2xl border border-line bg-glass backdrop-blur
                       shadow-2xl shadow-purple-500/10"
                >
                  <div class="p-3 sm:p-4 grid gap-3 sm:grid-cols-12 items-center">
                    <div class="sm:col-span-5">
                      <label class="sr-only">Search events</label>
                      <div
                        class="flex items-center gap-2 rounded-xl bg-surface-sunken border border-line px-3 py-2"
                      >
                        <span class="text-subtle">&#128269;</span>
                        <input
                          class="w-full bg-transparent outline-none text-sm placeholder:text-faint"
                          placeholder="Search artists, teams, venues..."
                          [(ngModel)]="heroSearch"
                          (keyup.enter)="explore()"
                        />
                      </div>
                    </div>
                    <div class="sm:col-span-4">
                      <label class="sr-only">Location</label>
                      <div
                        class="flex items-center gap-2 rounded-xl bg-surface-sunken border border-line px-3 py-2"
                      >
                        <span class="text-subtle">&#128205;</span>
                        <input
                          class="w-full bg-transparent outline-none text-sm placeholder:text-faint"
                          placeholder="Ottawa, Toronto..."
                          [(ngModel)]="heroCity"
                          (keyup.enter)="explore()"
                        />
                      </div>
                    </div>
                    <div class="sm:col-span-3">
                      <button
                        (click)="explore()"
                        class="inline-flex w-full items-center justify-center gap-2 rounded-xl px-4 py-2
                             bg-gradient-to-r from-purple-500 to-fuchsia-500 text-accent-contrast
                             font-semibold text-sm shadow-lg shadow-purple-500/25
                             hover:opacity-95 transition"
                      >
                        Explore events
                        <span>&rarr;</span>
                      </button>
                    </div>
                  </div>
                  <div class="px-4 pb-4 flex flex-wrap gap-2 text-xs text-subtle">
                    <button
                      class="rounded-full bg-glass border border-line px-2 py-1 hover:bg-glass-strong transition"
                      (click)="explore('Music')"
                    >
                      Concerts
                    </button>
                    <button
                      class="rounded-full bg-glass border border-line px-2 py-1 hover:bg-glass-strong transition"
                      (click)="explore('Sports')"
                    >
                      Sports
                    </button>
                    <button
                      class="rounded-full bg-glass border border-line px-2 py-1 hover:bg-glass-strong transition"
                      (click)="explore('Arts')"
                    >
                      Theatre
                    </button>
                    <button
                      class="rounded-full bg-glass border border-line px-2 py-1 hover:bg-glass-strong transition"
                      (click)="explore('Academic')"
                    >
                      Campus
                    </button>
                    <button
                      class="rounded-full bg-glass border border-line px-2 py-1 hover:bg-glass-strong transition"
                      (click)="explore('Party')"
                    >
                      Nightlife
                    </button>
                  </div>
                </div>
              }

              <div class="mt-8 grid grid-cols-3 gap-4 max-w-xl">
                <div class="rounded-2xl border border-line bg-glass p-4 backdrop-blur">
                  <div class="text-xl font-bold">4.9*</div>
                  <div class="text-xs text-subtle mt-1">avg rating</div>
                </div>
                <div class="rounded-2xl border border-line bg-glass p-4 backdrop-blur">
                  <div class="text-xl font-bold">2M+</div>
                  <div class="text-xs text-subtle mt-1">tickets delivered</div>
                </div>
                <div class="rounded-2xl border border-line bg-glass p-4 backdrop-blur">
                  <div class="text-xl font-bold">60s</div>
                  <div class="text-xs text-subtle mt-1">avg checkout</div>
                </div>
              </div>
            </div>

            <div class="lg:col-span-5">
              <div
                class="relative rounded-3xl border border-line bg-glass backdrop-blur
                       shadow-2xl shadow-fuchsia-500/10 overflow-hidden"
              >
                <div
                  class="absolute inset-0 bg-gradient-to-br from-purple-500/10 via-transparent to-fuchsia-500/10"
                ></div>

                <div class="relative p-5 sm:p-6">
                  <div class="flex items-center justify-between">
                    <div class="text-sm font-semibold text-content">Trending Now</div>
                    <div class="text-xs text-subtle">Updated live</div>
                  </div>

                  <div class="mt-4 grid gap-3">
                    @for (ev of trending; track trackByTitle($index, ev)) {
                      <div
                        class="group rounded-2xl border border-line bg-surface-sunken p-4
                               hover:bg-surface-sunken transition"
                      >
                        <div class="flex items-start justify-between gap-3">
                          <div>
                            <div class="flex items-center gap-2">
                              <div class="text-sm font-semibold">{{ ev.title }}</div>
                              @if (ev.badge) {
                                <span
                                  class="text-[10px] rounded-full px-2 py-0.5 border border-line bg-glass text-muted"
                                >
                                  {{ ev.badge }}
                                </span>
                              }
                            </div>
                            <div class="mt-1 text-xs text-subtle">
                              {{ ev.date }} &bull; {{ ev.venue }} &bull; {{ ev.city }}
                            </div>
                          </div>
                          <div class="text-right">
                            <div class="text-sm font-semibold">{{ ev.price }}</div>
                            <div class="text-[11px] text-subtle">from</div>
                          </div>
                        </div>
                        <div class="mt-3 flex items-center justify-between">
                          <div class="text-xs text-subtle">
                            <span class="text-muted">&bull;</span> Verified inventory
                          </div>
                          @if (eventsEnabled) {
                            <a
                              routerLink="/events"
                              class="text-xs font-semibold text-accent group-hover:text-content transition"
                            >
                              View seats &rarr;
                            </a>
                          }
                        </div>
                      </div>
                    }
                  </div>

                  <div class="mt-5 grid grid-cols-2 gap-3">
                    <div class="rounded-2xl border border-line bg-glass p-4">
                      <div class="text-xs text-subtle">Best deal</div>
                      <div class="mt-1 text-sm font-semibold">Smart pricing</div>
                      <div class="mt-2 text-xs text-subtle leading-relaxed">
                        Auto-suggest seats based on budget and sightlines.
                      </div>
                    </div>
                    <div class="rounded-2xl border border-line bg-glass p-4">
                      <div class="text-xs text-subtle">Instant delivery</div>
                      <div class="mt-1 text-sm font-semibold">Mobile tickets</div>
                      <div class="mt-2 text-xs text-subtle leading-relaxed">
                        Add to wallet in seconds, even last-minute.
                      </div>
                    </div>
                  </div>

                  @if (eventsEnabled) {
                    <div class="mt-5">
                      <a
                        routerLink="/events"
                        class="inline-flex w-full items-center justify-center gap-2 rounded-xl px-4 py-2
                             bg-inverse text-inverse-content font-semibold text-sm hover:opacity-90 transition"
                      >
                        Browse all events
                        <span>&rarr;</span>
                      </a>
                    </div>
                  }
                </div>
              </div>

              <div class="mt-5 flex flex-wrap gap-2 text-xs text-subtle">
                <span class="rounded-full border border-line bg-glass px-3 py-1"
                  >Stripe-ready</span
                >
                <span class="rounded-full border border-line bg-glass px-3 py-1"
                  >Anti-fraud</span
                >
                <span class="rounded-full border border-line bg-glass px-3 py-1"
                  >Organizer tools</span
                >
                <span class="rounded-full border border-line bg-glass px-3 py-1"
                  >Real-time seats</span
                >
              </div>
            </div>
          </div>
        </section>

        @if (eventsEnabled) {
          <section class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 mt-14">
            <div class="flex items-end justify-between gap-6">
              <div>
                <h2 class="text-xl sm:text-2xl font-bold">Browse by category</h2>
                <p class="mt-1 text-sm text-subtle">Pick a vibe. We'll handle the rest.</p>
              </div>
              <a
                routerLink="/events"
                class="text-sm font-semibold text-accent hover:text-content transition"
              >
                See all &rarr;
              </a>
            </div>
            <div class="mt-6 grid sm:grid-cols-2 lg:grid-cols-5 gap-4">
              @for (c of categories; track trackByName($index, c)) {
                <a
                  routerLink="/events"
                  class="group rounded-2xl border border-line bg-glass p-5 backdrop-blur
                       hover:bg-glass-strong hover:border-line-strong transition"
                >
                  <div class="flex items-center justify-between">
                    <div class="text-xs font-semibold uppercase tracking-[0.2em]">{{ c.icon }}</div>
                    <div
                      class="h-9 w-9 rounded-xl bg-gradient-to-br from-purple-500/20 to-fuchsia-500/20
                              border border-line flex items-center justify-center text-muted group-hover:text-content transition"
                    >
                      &rarr;
                    </div>
                  </div>
                  <div class="mt-4 text-sm font-semibold">{{ c.name }}</div>
                  <div class="mt-1 text-xs text-subtle">{{ c.count }}</div>
                </a>
              }
            </div>
          </section>
        }

        <section class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 mt-14">
          <div class="grid lg:grid-cols-12 gap-8 items-start">
            <div class="lg:col-span-4">
              <h2 class="text-xl sm:text-2xl font-bold">Built for speed and trust</h2>
              <p class="mt-2 text-sm text-subtle leading-relaxed">
                A booking flow that feels premium: verified tickets, transparent pricing, and modern
                organizer tools.
              </p>

              <a
                routerLink="/pricing"
                class="mt-5 inline-flex items-center gap-2 rounded-xl px-4 py-2
                       bg-gradient-to-r from-purple-500 to-fuchsia-500 text-sm font-semibold
                       shadow-lg shadow-purple-500/20 hover:opacity-95 transition"
              >
                See pricing
                <span>&rarr;</span>
              </a>
            </div>

            <div class="lg:col-span-8 grid sm:grid-cols-2 gap-4">
              @for (f of features; track trackByTitle($index, f)) {
                <div class="rounded-2xl border border-line bg-glass p-6 backdrop-blur">
                  <div class="flex items-center gap-3">
                    <div
                      class="h-11 w-11 rounded-2xl border border-line bg-gradient-to-br
                             from-purple-500/20 to-fuchsia-500/20 flex items-center justify-center"
                    >
                      <span class="text-lg">{{ f.icon }}</span>
                    </div>
                    <div class="text-sm font-semibold">{{ f.title }}</div>
                  </div>
                  <p class="mt-3 text-sm text-subtle leading-relaxed">
                    {{ f.desc }}
                  </p>
                </div>
              }
            </div>
          </div>
        </section>

        @if (eventsEnabled) {
          <section class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 mt-14">
            <div class="flex items-end justify-between gap-6">
              <div>
                <h2 class="text-xl sm:text-2xl font-bold">Popular this week</h2>
                <p class="mt-1 text-sm text-subtle">Curated picks near you.</p>
              </div>
              <a
                routerLink="/events"
                class="text-sm font-semibold text-accent hover:text-content transition"
              >
                Explore &rarr;
              </a>
            </div>
            <div class="mt-6 grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
              @for (e of popular; track trackByTitle($index, e)) {
                <a
                  routerLink="/events"
                  class="group rounded-3xl border border-line bg-glass overflow-hidden
                       hover:bg-glass-strong hover:border-line-strong transition"
                >
                  <div class="relative h-36">
                    <div
                      class="absolute inset-0 bg-gradient-to-br from-purple-500/35 via-fuchsia-500/15 to-indigo-500/25"
                    ></div>
                    <div
                      class="absolute inset-0 opacity-30"
                      style="
                      background-image: radial-gradient(circle at 20% 10%, rgba(255,255,255,0.45) 0, transparent 45%),
                                        radial-gradient(circle at 80% 60%, rgba(255,255,255,0.25) 0, transparent 50%);
                    "
                    ></div>
                    <div class="absolute top-4 left-4 flex gap-2">
                      <span
                        class="text-[10px] rounded-full px-2 py-1 border border-line bg-surface-sunken text-muted backdrop-blur"
                      >
                        Verified
                      </span>
                      @if (e.badge) {
                        <span
                          class="text-[10px] rounded-full px-2 py-1 border border-line bg-surface-sunken text-muted backdrop-blur"
                        >
                          {{ e.badge }}
                        </span>
                      }
                    </div>
                  </div>
                  <div class="p-5">
                    <div class="flex items-start justify-between gap-3">
                      <div>
                        <div class="text-sm font-semibold">{{ e.title }}</div>
                        <div class="mt-1 text-xs text-subtle">{{ e.date }}</div>
                        <div class="mt-1 text-xs text-subtle">
                          {{ e.venue }} &bull; {{ e.city }}
                        </div>
                      </div>
                      <div class="text-right">
                        <div class="text-sm font-semibold">{{ e.price }}</div>
                        <div class="text-[11px] text-subtle">from</div>
                      </div>
                    </div>
                    <div class="mt-4 flex items-center justify-between">
                      <div class="text-xs text-subtle">
                        <span class="text-muted">&bull;</span> Instant mobile tickets
                      </div>
                      <div
                        class="text-xs font-semibold text-accent group-hover:text-content transition"
                      >
                        View &rarr;
                      </div>
                    </div>
                  </div>
                </a>
              }
            </div>
          </section>
        }

        <section class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 mt-16">
          <div class="rounded-3xl border border-line bg-glass backdrop-blur p-6 sm:p-8">
            <div class="flex flex-col lg:flex-row lg:items-end lg:justify-between gap-6">
              <div>
                <h2 class="text-xl sm:text-2xl font-bold">Loved by fans and organizers</h2>
                <p class="mt-2 text-sm text-subtle max-w-2xl">
                  From pop-up campus events to arena shows - the experience stays fast, trustworthy,
                  and elegant.
                </p>
              </div>
              <a
                routerLink="/contact"
                class="inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2
                       bg-inverse text-inverse-content font-semibold text-sm hover:opacity-90 transition"
              >
                Talk to sales
                <span>&rarr;</span>
              </a>
            </div>

            <div class="mt-6 grid md:grid-cols-3 gap-4">
              @for (t of testimonials; track trackByName($index, t)) {
                <div class="rounded-2xl border border-line bg-surface-sunken p-6">
                  <p class="text-sm text-muted italic leading-relaxed">
                    &ldquo;{{ t.quote }}&rdquo;
                  </p>
                  <div class="mt-4 text-sm font-semibold">{{ t.name }}</div>
                  <div class="text-xs text-subtle">{{ t.role }}</div>
                </div>
              }
            </div>
          </div>
        </section>

        <section class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 mt-16 pb-16">
          <div
            class="rounded-3xl border border-line bg-gradient-to-r from-purple-500/20 via-fuchsia-500/10 to-indigo-500/20
                   p-8 sm:p-10 overflow-hidden relative"
          >
            <div
              class="absolute -top-24 -right-24 h-72 w-72 rounded-full bg-fuchsia-500/7 dark:bg-fuchsia-500/20 blur-3xl"
            ></div>
            <div
              class="absolute -bottom-24 -left-24 h-72 w-72 rounded-full bg-purple-500/7 dark:bg-purple-500/20 blur-3xl"
            ></div>

            <div class="relative grid lg:grid-cols-12 gap-8 items-center">
              <div class="lg:col-span-8">
                <h3 class="text-2xl sm:text-3xl font-extrabold tracking-tight">
                  Ready to launch your next event?
                </h3>
                <p class="mt-2 text-sm sm:text-base text-muted max-w-2xl">
                  Create listings, manage inventory, and sell tickets with a checkout that converts.
                </p>
              </div>

              <div class="lg:col-span-4 flex flex-col sm:flex-row lg:flex-col gap-3">
                @if (authEnabled) {
                  <a
                    routerLink="/auth/signup"
                    class="inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2
                         bg-inverse text-inverse-content font-semibold text-sm hover:opacity-90 transition"
                  >
                    Create an account
                    <span>&rarr;</span>
                  </a>
                }
                @if (eventsEnabled) {
                  <a
                    routerLink="/events"
                    class="inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2
                         border border-line-strong bg-glass text-content text-sm font-semibold
                         hover:bg-glass-strong transition"
                  >
                    Browse events
                    <span>&rarr;</span>
                  </a>
                }
              </div>
            </div>
          </div>

          <footer class="mt-10 text-xs text-subtle">
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
              <div>&copy; {{ year }} EventXperience. All rights reserved.</div>
              <div class="flex items-center gap-4">
                <a routerLink="/terms" class="hover:text-content transition">Terms</a>
                <a routerLink="/privacy" class="hover:text-content transition">Privacy</a>
                <a routerLink="/contact" class="hover:text-content transition">Support</a>
              </div>
            </div>
          </footer>
        </section>
      </main>
    </div>
  `,
})
export class HomeComponent {
  year = new Date().getFullYear();

  heroSearch = '';
  heroCity = '';
  readonly authEnabled: boolean;
  readonly eventsEnabled: boolean;

  constructor(
    private router: Router,
    private featureFlags: FeatureFlagsService,
  ) {
    this.authEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.auth);
    this.eventsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.events);
  }

  explore(category?: string): void {
    if (!this.eventsEnabled) {
      return;
    }

    const queryParams: Record<string, string> = {};

    if (this.heroSearch.trim()) {
      queryParams['search'] = this.heroSearch.trim();
    }

    if (this.heroCity.trim()) {
      queryParams['city'] = this.heroCity.trim();
    }

    if (category) {
      queryParams['category'] = category;
    }

    this.router.navigate(['/events'], { queryParams });
  }

  categories: Category[] = [
    { name: 'Concerts', icon: 'MU', count: '1,240 events' },
    { name: 'Sports', icon: 'SP', count: '540 events' },
    { name: 'Theatre', icon: 'TH', count: '320 events' },
    { name: 'Campus', icon: 'CA', count: '180 events' },
    { name: 'Festivals', icon: 'FE', count: '95 events' },
  ];

  features: Feature[] = [
    {
      icon: 'OK',
      title: 'Verified tickets',
      desc: 'Reduce fraud with identity and inventory checks and protected transfers.',
    },
    {
      icon: 'GO',
      title: 'Fast checkout',
      desc: 'Designed for conversion with saved payment methods and smart defaults.',
    },
    {
      icon: 'RT',
      title: 'Real-time seats',
      desc: 'Live availability and pricing - no more stale seat maps or surprises.',
    },
    {
      icon: 'MO',
      title: 'Mobile delivery',
      desc: 'Wallet-ready tickets with instant delivery and last-minute access.',
    },
  ];

  trending: EventCard[] = [
    {
      title: 'Neon Nights Tour',
      date: 'Fri - 7:30 PM',
      venue: 'Riverside Arena',
      city: 'Ottawa',
      price: '$49',
      badge: 'Hot',
    },
    {
      title: 'Capital City Hockey',
      date: 'Sat - 6:00 PM',
      venue: 'North Dome',
      city: 'Ottawa',
      price: '$35',
      badge: 'Few left',
    },
    {
      title: 'Indie Theatre Showcase',
      date: 'Sun - 8:00 PM',
      venue: 'Grand Hall',
      city: 'Toronto',
      price: '$28',
    },
  ];

  popular: EventCard[] = [
    {
      title: 'Skyline Music Festival',
      date: 'Jul 18-20',
      venue: 'Harbour Grounds',
      city: 'Toronto',
      price: '$89',
      badge: 'Weekend pass',
    },
    {
      title: 'UOttawa Winter Formal',
      date: 'Feb 22',
      venue: 'Student Union',
      city: 'Ottawa',
      price: '$15',
      badge: 'Campus',
    },
    {
      title: 'Championship Night',
      date: 'Mar 03 - 7:00 PM',
      venue: 'City Stadium',
      city: 'Montreal',
      price: '$55',
      badge: 'Trending',
    },
  ];

  testimonials: Testimonial[] = [
    {
      quote: 'The checkout is insanely smooth. I had my tickets in my wallet in under a minute.',
      name: 'Ayesha K.',
      role: 'Fan - Concerts',
    },
    {
      quote:
        'Our team sold out faster and support tickets dropped a lot. The organizer tools are clean.',
      name: 'Marco D.',
      role: 'Organizer - Campus events',
    },
    {
      quote: 'Love the transparent pricing - what you see is what you pay. Finally.',
      name: 'Jules P.',
      role: 'Fan - Sports',
    },
  ];

  trackByTitle = (_: number, item: { title: string }) => item.title;
  trackByName = (_: number, item: { name: string }) => item.name;
}
