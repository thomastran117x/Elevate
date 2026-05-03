import fs from 'fs';
import dotenv from 'dotenv';

dotenv.config();

const environment = {
  production: process.env.NODE_ENV === 'production',
  backendUrl: process.env.BACKEND_URL,
  frontendUrl: process.env.FRONTEND_URL,
  googleClientId: process.env.GOOGLE_CLIENT_ID,
  msalClientId: process.env.MSAL_CLIENT_ID,
  googleSiteKey: process.env.GOOGLE_SITE_KEY,
};

const filePath = './src/environments/environment.ts';

const content = `/**
 * ⚙️ Auto-generated from .env — do NOT edit manually.
    This file must be included in the .gitinore and not be
    shared in the public repository
 */
export const environment = ${JSON.stringify(environment, null, 2)} as const;
`;

fs.writeFileSync(filePath, content);
console.log('Generated src/environments/environment.ts from .env');
