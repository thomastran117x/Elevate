# Email worker

.NET 10 background application consuming API email messages from Kafka, rendering notification content, and sending mail with SMTP/MailKit. Event invitation messages also produce sent/failed delivery status consumed by the API.

See the [API README](../../../README.md), [configuration](../../../../docs/CONFIGURATION.md), [testing](../../../../docs/TESTING.md), and [CONTRIBUTING](../../../../CONTRIBUTING.md).

## Dependencies and settings

Provision Kafka and an SMTP account reachable by this process.

| Variable                       | Default / purpose                                                                            |
| ------------------------------ | -------------------------------------------------------------------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`      | Shared brokers; standalone fallback `localhost:9092`, template/Compose `kafka:9092`          |
| `EMAIL_TOPIC`                  | `eventxperience-email`; align with API publisher                                             |
| `EMAIL_GROUP_ID`               | `email-worker`                                                                               |
| `EMAIL_DLQ_TOPIC`              | `eventxperience-email-dlq`                                                                   |
| `EMAIL_STATUS_TOPIC`           | `eventxperience-email-status`; align with API status consumer                                |
| `SMTP_SERVER`                  | Required SMTP hostname; `gmail`/`google`, Outlook-family aliases, and `yahoo` are normalized |
| `SMTP_PORT`                    | 587                                                                                          |
| `EMAIL_USER`, `EMAIL_PASSWORD` | Required SMTP credentials                                                                    |
| `FRONTEND_URL`                 | Public link origin, default `http://localhost:3090`                                          |

Without SMTP server/user/password, the consumer logs a warning and exits its background task; the host can remain running without delivering mail. Ancestor `.env` loading is used outside containers; process variables win. Consult [setup](../../../../docs/SETUP.md) for host Kafka connectivity.

## Run and Docker

From the repository root:

```powershell
dotnet run --project backend/src/worker/email-worker/email-worker.csproj
docker compose up -d email-worker
docker compose logs -f email-worker
docker build -f backend/src/worker/email-worker/Dockerfile -t eventxperience-email-worker:local backend
```

Supply settings before starting. The standalone Docker build uses `backend/` as context; Compose supplies networking and environment. This consumer has no HTTP API.

## Processing and failures

The processor deserializes the shared `EmailMessage` contract, validates the recipient, renders content according to message type, and sends it. SMTP delivery retries up to three times with exponential delay starting at 500 ms. Malformed payloads and exhausted failures are published to the DLQ. For invitation-linked messages, sent/failed status is also published; failure of status publication can enter the processing-failure path.

Kafka auto-commit is disabled; a group without offsets starts at earliest. The consumer commits when processing returns, including successful DLQ publication. DLQ/status publication errors that escape processing leave the offset uncommitted and trigger a five-second reconnect delay. Cancellation propagates.

Sending and committing are not atomic: a retry after SMTP success can send duplicate mail. Monitor notification lag, delivery/status failures, and DLQs. A successful SMTP submission is not proof of inbox delivery. Inspect status failures before replaying messages.

## Tests

From the repository root:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.Email"
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.Worker"
```

Tests cover rendering, delivery processing, SMTP behavior with doubles, and shared worker options/publishers/execution. Integration probes verify API notification contracts through Kafka; real SMTP credentials are unnecessary for those assertions.
