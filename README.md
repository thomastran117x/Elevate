# EventXperience

EventXperience is a web application for discovering and organizing events and building club communities. It supports accounts and MFA, public profiles, clubs and discussions, event invitations, recurring events, registration and waitlists, favourites, analytics, and payment integration.

The frontend uses Angular 22, TypeScript, NgRx, Tailwind CSS, and Angular SSR. The API and five background workers use .NET 10. PostgreSQL stores application data; Redis supports caching and shared state; Kafka carries notifications and search updates; Elasticsearch powers search.

## Quick start with Docker

Run from the repository root with Docker Compose v2 and a running Docker daemon:

```powershell
Copy-Item .env.example .env # only if .env does not already exist
docker compose up --build -d
docker compose ps
docker compose logs -f backend
```

The API applies EF Core migrations at normal startup. Allow time for infrastructure and connector initialization. The frontend is at <http://localhost:3090>; the API base is <http://localhost:8090/api>. Email, SMS, OAuth, captcha, payments, and Azure uploads require provider configuration. The supplied frontend container runs the development server; see [deployment](docs/DEPLOYMENT.md) for built SSR serving and deployment limitations.

See [setup](docs/SETUP.md) for prerequisites, local development, infrastructure addresses, and troubleshooting. Stop the stack with `docker compose down`; this preserves named volumes.

## Repository map

| Component                                                          | Purpose                                                    |
| ------------------------------------------------------------------ | ---------------------------------------------------------- |
| [Frontend](frontend/README.md)                                     | Angular UI, SSR server, and browser tests                  |
| [Backend API](backend/README.md)                                   | ASP.NET Core application and EF Core migrations            |
| [Event indexer](backend/src/worker/event-indexer/README.md)        | Event search updates                                       |
| [Club indexer](backend/src/worker/club-indexer/README.md)          | Club search updates                                        |
| [Club-post indexer](backend/src/worker/clubpost-indexer/README.md) | Club-post search updates                                   |
| [Email worker](backend/src/worker/email-worker/README.md)          | SMTP notifications and invitation delivery status          |
| [SMS worker](backend/src/worker/sms-worker/README.md)              | Twilio MFA messages                                        |
| [Backend unit tests](backend.tests.Unit/README.md)                 | Isolated behavior tests                                    |
| [Backend integration tests](backend.tests.Integration/README.md)   | Real API and container-backed infrastructure tests         |
| [Development tasks](tools/Event.DevTasks/README.md)                | Formatting, coverage, integration runs, and OpenAPI export |

`docker/` contains Kafka Connect outbox configuration. `backend.sln` groups the .NET projects; `global.json` selects the SDK.

## Documentation

- [Setup](docs/SETUP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Configuration](docs/CONFIGURATION.md)
- [Developer conventions](docs/DEVELOPERS.md)
- [API guide](docs/API.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Testing and coverage policy](docs/TESTING.md)

Read [CONTRIBUTING.md](CONTRIBUTING.md) before making changes. Coding agents must follow [AGENTS.md](AGENTS.md); Claude loads the same instructions through [CLAUDE.md](CLAUDE.md).
