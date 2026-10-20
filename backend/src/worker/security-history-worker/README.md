# Security history worker

.NET 10 background application that keeps the account security history (#326). It consumes the entries the API publishes to the security-history topic and appends them to `AccountSecurityEvents`. It emails the owner about high-risk changes and deletes entries past their 90-day retention. The host registers a scoped writer, alert sender and message processor, a DLQ publisher, a Kafka consumer, and the retention purge.

This is the only worker that connects to PostgreSQL. It writes one table in the API's database and reads the owner's address from `Users` to send an alert. The API owns the schema and its migrations. The worker waits until `AccountSecurityEvents` exists before consuming, so it can start first.

See [security events](../../../../docs/SECURITY_EVENTS.md#account-security-history) for what is recorded and why, the [API README](../../../README.md), [configuration](../../../../docs/CONFIGURATION.md#account-security-history), and [CONTRIBUTING](../../../../CONTRIBUTING.md).

## Dependencies and settings

You need Kafka and the API's PostgreSQL database. Alerts also need the email worker.

| Variable                                  | Default / purpose                                                                                                                                                                              |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`                 | Shared broker setting. Standalone fallback `localhost:9092`, template/Compose `kafka:9092`                                                                                                     |
| `DB_CONNECTION_STRING`                    | The API's database                                                                                                                                                                             |
| `SECURITY_HISTORY_TOPIC`                  | `eventxperience-security-history`. Entries published by the API                                                                                                                                |
| `SECURITY_HISTORY_GROUP_ID`               | `security-history-worker`                                                                                                                                                                      |
| `SECURITY_HISTORY_DLQ_TOPIC`              | `eventxperience-security-history-dlq`                                                                                                                                                          |
| `SECURITY_HISTORY_RETENTION_DAYS`         | `90`, 1–90. Owners are told entries are kept for up to 90 days, so it can only shorten that. Fixed into each entry's `ExpiresAtUtc` when it is stored, so a change applies to new entries only |
| `SECURITY_HISTORY_PURGE_INTERVAL_MINUTES` | `60`, 1–1440                                                                                                                                                                                   |
| `SECURITY_HISTORY_PURGE_BATCH_SIZE`       | `500`, 1–5000 rows per delete                                                                                                                                                                  |
| `SECURITY_HISTORY_PURGE_MAX_BATCHES`      | `20`, 1–1000 batches per pass. Whatever is left waits for the next pass                                                                                                                        |
| `SECURITY_HISTORY_NOTIFICATIONS_ENABLED`  | `true`. Email the owner about alert kinds                                                                                                                                                      |
| `SECURITY_HISTORY_ALERT_KINDS`            | Comma-separated kinds, or `none`. Unset means `password_changed,provider_linked,refresh_token_reuse_detected,account_disabled`                                                                 |
| `EMAIL_TOPIC`                             | `eventxperience-email`. Where alerts are handed to the email worker                                                                                                                            |

The worker refuses to start if a value is out of range or names an unknown kind. The default alert kinds are the high-risk changes that send nothing of their own. MFA changes, email changes, and password resets already email the owner when they happen, so adding those kinds sends two emails for one change. Options come from the environment. Non-container runs load ancestor `.env` files, and process values take precedence.

## Run and Docker

From the repository root, with infrastructure and environment settings ready:

```powershell
dotnet run --project backend/src/worker/security-history-worker/security-history-worker.csproj
docker compose up -d security-history-worker
docker compose logs -f security-history-worker
```

For a standalone image build, also from the root:

```powershell
docker build -f backend/src/worker/security-history-worker/Dockerfile -t eventxperience-security-history-worker:local backend
```

Use `backend/` as the build context, because the worker shares the entity, message contract, and notification service with the API. This is a background consumer with no HTTP API.

## Processing and failures

`SecurityHistoryMessageParser` holds each payload to the producer's contract before anything is stored:

- known kinds, clients, and device classes;
- a recorded catalog event name;
- known method, factor, and reason names;
- a network that is exactly a masked /24 or /48 prefix;
- a well-formed request id.

`SecurityHistoryMessageProcessor` handles the outcomes like this:

- **A new entry** is inserted, then alerted on if its kind is an alert kind.
- **A redelivery** matches the unique `MessageId` and changes nothing. It never alerts again.
- **An entry for a deleted account** fails the foreign key and is dropped. The history went with the account.
- **An entry already past retention**, for example after a long outage, is skipped.
- **Malformed payloads and exhausted retries** go to the DLQ. Storing is retried up to three times, with an exponential delay starting at 500 ms.
- **A DLQ publish that fails** propagates. The offset stays uncommitted, the consumer reconnects after five seconds, and the message runs again.

The consumer starts at the earliest offset when its group has no committed position, and it disables auto-commit. It commits after processing returns, including after a successful DLQ publication.

Alerts are best effort. The owner's address is read from `Users` when the alert is sent and never travels on the topic. A failed alert is logged by exception type and counted. It is never retried, because a retry could duplicate the history row or hold up the queue. A crash between the insert and the alert loses that alert.

## Retention

`SecurityHistoryRetentionService` runs `SecurityHistoryRetentionRunner` on startup and then every purge interval. Each pass deletes entries whose `ExpiresAtUtc` has passed, oldest first, in bounded batches. Each pass logs how many it deleted when that is more than zero. The API also hides expired entries, so nothing is shown between expiry and the next pass. Every replica purges; the deletes are idempotent.

## Metrics

The `EventXperience.SecurityHistory` meter has three counters:

- `security_history.entries` by `outcome` (`inserted`, `duplicate`, `account_deleted`, `expired`, `rejected`, `failed`);
- `security_history.alerts` by `outcome` (`sent`, `failed`, `no_recipient`);
- `security_history.purged`.

Read them with `dotnet-counters monitor --counters EventXperience.SecurityHistory` until an exporter is chosen (#330). Watch the DLQ and `failed` entries.

## Tests

From the repository root:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.SecurityHistory"
```

The unit project references this worker, so `security-history-worker.dll` is measured by the coverage gate. Unit tests drive the consume loop against a mocked consumer, and run the writer, alerts, and purge against SQLite. The integration project references the worker under the `securityhistoryworker` extern alias, because its top-level `Program` would otherwise clash with the API's. `SecurityHistoryPipelineTests` reads real API messages off Kafka and runs them through the processor against PostgreSQL. See [testing](../../../../docs/TESTING.md) for prerequisites and coverage boundaries.
