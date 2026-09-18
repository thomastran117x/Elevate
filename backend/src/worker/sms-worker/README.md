# SMS worker

.NET 10 background application consuming MFA SMS messages from Kafka and submitting them to Twilio over HTTP. The API publishes the shared `SmsMfaMessage` contract; the worker formats verification text and chooses the configured sender.

See the [API README](../../../README.md), [configuration](../../../../docs/CONFIGURATION.md), [testing](../../../../docs/TESTING.md), and [CONTRIBUTING](../../../../CONTRIBUTING.md).

## Dependencies and settings

Provision Kafka, Twilio credentials, and network access to Twilio's API.

| Variable                                  | Default / purpose                                                                   |
| ----------------------------------------- | ----------------------------------------------------------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`                 | Shared brokers; standalone fallback `localhost:9092`, template/Compose `kafka:9092` |
| `SMS_TOPIC`                               | `eventxperience-sms`; align with API publisher                                      |
| `SMS_GROUP_ID`                            | `sms-worker`                                                                        |
| `SMS_DLQ_TOPIC`                           | `eventxperience-sms-dlq`                                                            |
| `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` | Required credentials                                                                |
| `TWILIO_MESSAGING_SERVICE_SID`            | Preferred sender configuration                                                      |
| `TWILIO_FROM_PHONE_NUMBER`                | Alternative sender when no messaging service is supplied                            |

The consumer is enabled only with account SID, auth token, and at least one sender setting. Otherwise it logs a warning and stops its background task; the host can remain running. API SMS enrollment/enforcement/step-up switches control MFA behavior separately from this consumer's credential check.

Non-container runs load ancestor `.env` files; process variables win. See setup for the host Kafka listener limitation. Keep OTPs and credentials out of documentation and diagnostic output.

## Run and Docker

From the repository root:

```powershell
dotnet run --project backend/src/worker/sms-worker/sms-worker.csproj
docker compose up -d sms-worker
docker compose logs -f sms-worker
docker build -f backend/src/worker/sms-worker/Dockerfile -t eventxperience-sms-worker:local backend
```

Supply settings before starting. The Docker context is `backend/`; Compose supplies network/environment configuration. This consumer exposes no HTTP API.

## Processing and failures

The sender validates phone/code fields and posts to Twilio. HTTP 429 and 5xx responses become transient delivery errors; transient errors and HTTP request exceptions retry up to three times with exponential delay starting at 500 ms. Permanent errors and malformed payloads go directly to the DLQ path.

Kafka auto-commit is disabled and a group without offsets starts at earliest. The consumer commits after processing returns. **Current SMS-specific policy:** if DLQ publication fails, the processor logs the failure and suppresses it so the consumer can advance. A failed message can therefore be lost without a DLQ record. Monitor those log failures; do not assume notification delivery is guaranteed by a committed offset.

Other consumer failures reconnect after five seconds. Shutdown cancellation propagates. Submission and Kafka commit are not atomic, so duplicate delivery is also possible after interruption. MFA codes expire; consider expiry and recipient impact before deliberate replay.

## Tests

From the repository root:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.Sms"
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.Worker"
```

Tests cover Twilio request formatting/status handling, transient errors, processor/DLQ behavior, and shared worker options/publishers/execution. Kafka-backed integration probes verify API SMS messages without contacting Twilio.
