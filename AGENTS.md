# Project instructions for coding agents

Read [CONTRIBUTING.md](CONTRIBUTING.md), [developer conventions](docs/DEVELOPERS.md), and the README for each component you change. The root [README](README.md) indexes all components; [architecture](docs/ARCHITECTURE.md), [configuration](docs/CONFIGURATION.md), [API](docs/API.md), and [testing](docs/TESTING.md) describe shared contracts.

## Git workflow: required

- Never edit or commit directly on `main`. Before making changes, inspect `git status --short` and `git branch --show-current`.
- If on `main` or a detached HEAD, create and check out a descriptive work branch with `git switch -c <branch>` before editing. Reuse an existing branch only when it belongs to this task. Do not push directly to `main`.
- Preserve unrelated user changes. Do not reset, discard, overwrite, or stage them. Do not switch branches in a way that loses work.
- Organize work into multiple logical commits when the task has distinct changes. Stage explicit paths or hunks, inspect `git diff --cached`, validate the group, and commit each coherent purpose separately. Keep coupled code/tests/contracts together so intermediate commits remain consistent; do not split an indivisible fix artificially.
- Avoid blanket staging, secrets, local environments, generated dependencies, and build output. Do not amend others' commits or rewrite shared history without explicit authorization.

## Pull requests and reviews

When asked to open a pull request, keep it focused and include:

- **Summary:** a concise overview of the outcome.
- **Context:** the issue, bug, or user need that prompted the change, including a link or identifier when available.
- **Changes made:** the important implementation and documentation changes.
- **How to test:** clear verification steps and the checks that were run, distinguishing passed checks from checks that could not be run.
- **Screenshots:** before-and-after or resulting UI evidence for frontend changes.
- **Reviewer notes:** migrations, configuration changes, known limitations, follow-up work, or anything else that will help the reviewer.

Screenshots and reviewer notes may be omitted when they are not relevant.

When reviewing a pull request, assess both behavioral correctness and code quality. In addition to defects and regressions, consider maintainability, clarity, unnecessary complexity, duplication, consistency with the established architecture, and the quality of the tests. Treat material correctness risks, architectural violations, and maintainability problems as blocking findings. Present minor improvements as non-blocking suggestions, and avoid blocking solely on personal style preferences that are not established project conventions.

## Project boundaries

The frontend is Angular 22/TypeScript with SSR, NgRx, RxJS, Tailwind, and ZoneJS. All .NET projects target .NET 10; `global.json` selects the SDK. The backend uses PostgreSQL/EF Core, Redis, Elasticsearch, and Kafka. Five standalone workers handle search indexing and notifications.

Follow neighboring names, imports, namespaces, file placement, and formatting. Keep changes within the requested scope. Preserve established architecture and contracts; avoid incidental module/store/ZoneJS/hydration migrations or dependency upgrades. Verify conventions against code when documentation and implementation disagree, and update stale documentation in scope.

## Angular requirements

- Place feature code in `frontend/src/app/features/`, application infrastructure in `core/`, and reusable UI in `shared/`. Use existing TypeScript aliases and strict types; narrow unknown payloads instead of adding `any`.
- Prefer compatible standalone components, signals/computed local state, modern template control flow, and OnPush-compatible immutable updates for new code. Preserve existing NgModules/change detection where required, NgRx shared state, and RxJS asynchronous contracts.
- Keep HTTP logic in services and preserve response envelopes, camelCase/PascalCase normalizers, auth/refresh/CSRF interceptors, and feature route/navigation behavior.
- Use async pipes or `takeUntilDestroyed` in a valid injection context; clean up subscriptions, timers, and event listeners. Guard browser-only APIs for SSR and preserve consistent hydration. Do not expose server secrets through generated frontend configuration.
- Use semantic markup, accessible labels/errors, keyboard operation, and appropriate focus behavior. Follow configured Prettier settings and format only affected paths.
- Add meaningful colocated specs for new services, guards, interceptors, and normalizers. Reuse `@testing`; isolate environment/store/storage changes and verify HTTP expectations.

References: [Angular style](https://angular.dev/style-guide), [signals](https://angular.dev/guide/signals), [SSR](https://angular.dev/guide/ssr), and [accessibility](https://angular.dev/best-practices/a11y).

## .NET requirements

- Preserve feature organization and controller/service/repository responsibilities. Use DTOs and shared API response/error conventions; register services through existing bootstrap code with appropriate lifetimes.
- Use async I/O, propagate supported cancellation tokens, and avoid `.Result`/`.Wait()` or unnecessary `Task.Run` around I/O. Never perform concurrent EF operations on the same DbContext. Workers must create scopes for scoped dependencies.
- Project needed data, paginate large reads, avoid N+1 queries, and use no-tracking reads when tracking is unnecessary. Preserve repository/cache resilience and transaction behavior.
- Treat the database as authoritative. Keep outbox writes consistent with committed changes. Preserve search fallback behavior and worker retry/commit/DLQ policies unless explicitly changing them with tests.
- Preserve authorization, ownership, feature gates, validation, CSRF, rate limits, MFA, and session invalidation. Do not log secrets or weaken security to make tests pass.
- Generate migrations for model changes using the EF tool; review the output. Do not hand-edit generated designers/snapshots or rewrite deployed migrations. Regenerate paired OpenAPI artifacts for contract changes and review feature-sensitive output.

References: [ASP.NET Core best practices](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0), [DI lifetimes](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#service-lifetimes), [EF queries](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying), and [EF async](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async).

## Validation and documentation

Run appropriate checks before each logical commit. From the root, use `git diff --check`, `dotnet format backend.sln --verify-no-changes`, relevant unit tests, and `Event.DevTasks` coverage/integration/endpoint-audit commands. From `frontend/`, generate the environment, then run relevant formatting, type, build, Karma, and Playwright checks. See [testing](docs/TESTING.md) and [CONTRIBUTING](CONTRIBUTING.md) for exact commands/prerequisites.

Do not weaken coverage floors (backend filtered lines 90%; frontend all four metrics 90%), tests, audits, or build budgets. Report what ran, what passed, and what could not run accurately. Documentation-only changes need links/paths/commands and whitespace verification; do not run full application suites without a reason.

Update guides and component READMEs for changes to setup, configuration, contracts, testing, or deployment. Edit the generator rather than `frontend/src/environments/environment.ts`; keep secrets and artifacts untracked. Maintain shared agent rules here so Claude and other agents stay aligned.
