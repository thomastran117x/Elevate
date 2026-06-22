import fs from 'fs';
import dotenv from 'dotenv';

dotenv.config();

const featureFlagEnvMap = {
  auth: 'FEATURE_AUTH',
  clubs: 'FEATURE_CLUBS',
  'clubs.follow': 'FEATURE_CLUBS_FOLLOW',
  'clubs.posts': 'FEATURE_CLUBS_POSTS',
  'clubs.reviews': 'FEATURE_CLUBS_REVIEWS',
  'clubs.versioning': 'FEATURE_CLUBS_VERSIONING',
  events: 'FEATURE_EVENTS',
  'events.analytics': 'FEATURE_EVENTS_ANALYTICS',
  'events.images': 'FEATURE_EVENTS_IMAGES',
  'events.invitations': 'FEATURE_EVENTS_INVITATIONS',
  'events.registration': 'FEATURE_EVENTS_REGISTRATION',
  'events.versioning': 'FEATURE_EVENTS_VERSIONING',
  payment: 'FEATURE_PAYMENT',
  'profile.admin': 'FEATURE_PROFILE_ADMIN',
  search: 'FEATURE_SEARCH',
  'search.reindex': 'FEATURE_SEARCH_REINDEX',
};

function parseBoolean(value, envName) {
  if (value === undefined || value === null || value === '') {
    return undefined;
  }

  if (value === 'true') {
    return true;
  }

  if (value === 'false') {
    return false;
  }

  throw new Error(`Expected ${envName} to be true or false.`);
}

const featureFlags = Object.fromEntries(
  Object.entries(featureFlagEnvMap)
    .map(([key, envName]) => [key, parseBoolean(process.env[envName], envName)])
    .filter(([, value]) => value !== undefined),
);

const environment = {
  production: process.env.NODE_ENV === 'production',
  backendUrl: process.env.BACKEND_URL,
  frontendUrl: process.env.FRONTEND_URL,
  googleClientId: process.env.GOOGLE_CLIENT_ID,
  msalClientId: process.env.MSAL_CLIENT_ID,
  googleSiteKey: process.env.GOOGLE_SITE_KEY,
  featureFlags,
};

const filePath = './src/environments/environment.ts';

const content = `/**
 * Auto-generated from .env. Do not edit manually.
 */
import type { FeatureFlags } from '../app/core/features/feature-flags.types';

export const environment: {
  production: boolean;
  backendUrl: string;
  frontendUrl: string;
  googleClientId: string;
  msalClientId: string;
  googleSiteKey: string;
  featureFlags: FeatureFlags;
} = ${JSON.stringify(environment, null, 2)};
`;

fs.writeFileSync(filePath, content);
console.log('Generated src/environments/environment.ts from .env');
