# Frontend

Angular 22 UI and SSR application for EventXperience, using strict TypeScript, NgRx user/session state, RxJS, Tailwind CSS, and ZoneJS. Features live in `src/app/features/`, application infrastructure in `core/`, reusable UI in `shared/`, and shared test doubles in `src/testing/`. Existing NgModules and standalone components coexist.

Read [setup](../docs/SETUP.md), [configuration](../docs/CONFIGURATION.md), and [CONTRIBUTING](../CONTRIBUTING.md). Commands below run from `frontend/`.

## Development

Install Node.js 24 or newer. Prepare `frontend/.env` from the root template if needed; the generator reads this local file and process variables, not the root file automatically.

```powershell
npm ci
npm start
```

`npm start` generates `src/environments/environment.ts` and serves at <http://localhost:3090>. Do not hand-edit the generated environment. Only public values are embedded: API/frontend URLs, OAuth client IDs, captcha site key, production flag, and frontend flags.

For same-origin API access, use `BACKEND_URL=http://localhost:3090/api` in the frontend environment. `proxy.conf.mjs` forwards `/api` and WebSockets to `http://localhost:8090`; an exported `API_PROXY_TARGET` overrides it. The generator's dotenv changes do not propagate into its parent process.

## Build and SSR

```powershell
npm run generate:env
npm run build
npm run serve:ssr:frontend
```

The production build produces `dist/frontend/browser/` and `dist/frontend/server/server.mjs`. The built Express server defaults to port 3090, serves Angular SSR/static assets, proxies `/api`, and tunnels hub WebSockets. Configure `PORT`, `API_PROXY_TARGET`, and `NG_ALLOWED_HOSTS` in the server process. Generate public production configuration with `NODE_ENV=production` before building.

Hydration enables event replay, disables HTTP transfer caching and incremental hydration, and retains ZoneJS. Preserve browser/server rendering consistency and guard browser-only globals. The supplied Dockerfile runs the dev server, not this built SSR entry; see [deployment](../docs/DEPLOYMENT.md).

## Checks

```powershell
npm run generate:env
npm run format:check
npm run typecheck
npm run build
npm test -- --watch=false --browsers=ChromeHeadlessCI --code-coverage
npm run playwright:install
npm run test:e2e
```

Karma/Jasmine tests are colocated `*.spec.ts` files. Chrome/Chromium is required; coverage enforces 90% for all four metrics. Use `@testing` helpers for HTTP, NgRx, flags, routes, storage, and fixtures.

Playwright tests live in `tests/`; they start the E2E dev server on `http://127.0.0.1:3101`. API-backed journeys require backend state; the existing home smoke test checks rendered UI. See [testing](../docs/TESTING.md) for filters and policy.

Prettier uses single quotes, semicolons, trailing commas, and width 100. Format affected paths with `npx prettier --write <paths>`; `npm run format` rewrites the whole frontend.

## Agent tools

`.mcp.json` registers Playwright and Angular CLI MCP; launch workspace-dependent tools from this directory. VS Code has an equivalent `.vscode/mcp.json`. Shared agent rules live in the root [AGENTS.md](../AGENTS.md) and [CLAUDE.md](../CLAUDE.md).
