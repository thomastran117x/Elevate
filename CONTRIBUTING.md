# Contributing

Start with the [setup guide](docs/SETUP.md), [developer conventions](docs/DEVELOPERS.md), and the README for the component you will change. Local development uses Node.js 24 or newer, the .NET SDK selected by `global.json`, and Docker for container-backed integration tests.

## Branches and commits

Never make changes or commits directly on `main`. Inspect the working tree first:

```powershell
git status --short
git branch --show-current
git switch -c docs/describe-your-change
```

Create a descriptive work branch before editing if you are on `main` or a detached HEAD. An existing branch belonging to the task can be reused. Choose a name appropriate to the work, such as `feat/event-reminders`, `fix/registration`, or `docs/setup`. Preserve unrelated changes; do not reset or overwrite another contributor's work to obtain a clean tree.

Organize work into multiple logical commits when it contains distinct changes. Each commit should have one clear purpose and leave the repository consistent. Stage explicit paths or use `git add -p`, then inspect `git diff --cached` and run the relevant checks before committing. Avoid staging unrelated changes, secrets, generated environments, or build artifacts. Small indivisible fixes can be one commit; do not split coupled changes into broken intermediate states.

Use concise commit subjects describing the change. Push the work branch and open a pull request against `main`; do not push directly to `main`. Do not rewrite shared history without agreement.

## Implementation conventions

Keep changes within the requested feature or fix and follow neighboring code. Preserve the Angular feature/core/shared structure, the backend controller/service/repository boundaries, and existing security and API contracts. Avoid incidental dependency upgrades or framework migrations. See [developer conventions](docs/DEVELOPERS.md) for framework guidance and generated-file rules.

Update documentation alongside changes to setup, configuration, API behavior, testing, or deployment. Keep common policy in the core guides and component-specific instructions in component READMEs. Use relative Markdown links and state the working directory for command examples.

## Validation

Run the checks appropriate to your change. From the repository root:

```powershell
git diff --check
dotnet format backend.sln --verify-no-changes
dotnet test backend.tests.Unit/backend.tests.Unit.csproj
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

For backend behavior involving persistence, endpoints, or infrastructure, also run relevant integration tests with Docker running. The endpoint audit with `--fail-on-missing` is enforced in CI. See [testing](docs/TESTING.md) for commands and coverage requirements.

From `frontend/`:

```powershell
npm ci
npm run generate:env
npm run format:check
npm run typecheck
npm run build
npm test -- --watch=false --browsers=ChromeHeadlessCI --code-coverage
```

Use Playwright for affected browser journeys. Format only affected frontend files with Prettier; the broad `npm run format` rewrites the workspace. Documentation-only changes need link/path/command verification and whitespace checks, rather than the full application suites.

CI also audits dependencies: NuGet high/critical advisories, npm production high advisories, and npm moderate advisories across all dependencies. Do not weaken tests, coverage floors, build budgets, or audit gates to pass a change.

## Pull requests

Keep the pull request focused and structure its description with:

- **Summary:** a concise overview of the outcome.
- **Context:** the issue, bug, or user need that prompted the change, including a link or identifier when available.
- **Changes made:** the important implementation and documentation changes.
- **How to test:** clear verification steps and the checks that were run, distinguishing passed checks from checks that could not be run.
- **Screenshots:** before-and-after or resulting UI evidence for frontend changes.
- **Reviewer notes:** migrations, configuration changes, known limitations, follow-up work, or anything else that will help the reviewer.

Screenshots and reviewer notes may be omitted when they are not relevant. Keep commits reviewable. Review is completed through the repository's pull-request process; the `ai-review` label additionally triggers the configured Claude review workflow for non-draft PRs.
