export const FEATURE_KEYS = {
  auth: 'auth',
  clubs: 'clubs',
  clubsFollow: 'clubs.follow',
  clubsPosts: 'clubs.posts',
  clubsReviews: 'clubs.reviews',
  clubsVersioning: 'clubs.versioning',
  events: 'events',
  eventsAnalytics: 'events.analytics',
  eventsImages: 'events.images',
  eventsInvitations: 'events.invitations',
  eventsRegistration: 'events.registration',
  eventsVersioning: 'events.versioning',
  payment: 'payment',
  profile: 'profile',
  profileAdmin: 'profile.admin',
  search: 'search',
  searchReindex: 'search.reindex',
} as const;

export type FeatureKey = (typeof FEATURE_KEYS)[keyof typeof FEATURE_KEYS];
export type FeatureFlags = Partial<Record<FeatureKey, boolean>>;
